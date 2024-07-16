using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace SocketAPI
{
    /// <summary>
    /// Acts as an API server, accepting requests and replying over TCP/IP.
    /// </summary>
    public sealed class SocketAPIServer : IDisposable
    {
        private readonly CancellationTokenSource tcpListenerCancellationSource = new();
        private CancellationToken tcpListenerCancellationToken => tcpListenerCancellationSource.Token;
        private TcpListener? listener;
        private readonly Dictionary<string, Delegate> apiEndpoints = new();
        private readonly ConcurrentBag<TcpClient> clients = new();
        private static SocketAPIServer? _shared;

        private SocketAPIServer() { }

        public static SocketAPIServer Shared => _shared ??= new SocketAPIServer();

        public async Task Start(SocketAPIServerConfig config)
        {
            if (!config.Enabled)
                return;

            if (!config.LogsEnabled)
                Logger.DisableLogs();

            Logger.LogInfo($"Number of registered endpoints: {RegisterEndpoints()}");

            listener = new TcpListener(IPAddress.Any, config.Port);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                Logger.LogError($"Socket API server failed to start: {ex.Message}");
                return;
            }

            Logger.LogInfo($"Socket API server listening on port {config.Port}.");

            tcpListenerCancellationToken.ThrowIfCancellationRequested();
            tcpListenerCancellationToken.Register(() =>
            {
                if (listener != null)
                {
                    listener.Stop();
                }
            });

            await AcceptClientsAsync();
        }

        private async Task AcceptClientsAsync()
        {
            while (!tcpListenerCancellationToken.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await listener?.AcceptTcpClientAsync()!;
                    clients.Add(client);

                    IPEndPoint? clientEP = client.Client.RemoteEndPoint as IPEndPoint;
                    Logger.LogInfo($"Client connected! IP: {clientEP?.Address}, Port: {clientEP?.Port}");

                    _ = HandleTcpClientAsync(client);
                }
                catch (OperationCanceledException) when (tcpListenerCancellationToken.IsCancellationRequested)
                {
                    Logger.LogInfo("Socket API server was closed.");
                    while (!clients.IsEmpty)
                        clients.TryTake(out _);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error on the socket API server: {ex.Message}");
                }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client)
        {
            using NetworkStream stream = client.GetStream();

            try
            {
                byte[] buffer = new byte[client.ReceiveBufferSize];
                while (!tcpListenerCancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, tcpListenerCancellationToken);
                    if (bytesRead == 0)
                    {
                        Logger.LogInfo("Remote client closed the connection.");
                        break;
                    }

                    string rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    SocketAPIRequest? request = SocketAPIProtocol.DecodeMessage(rawMessage);

                    if (request == null)
                    {
                        SendResponse(client, SocketAPIMessage.FromError("Error parsing request."));
                        continue;
                    }

                    SocketAPIMessage? message = InvokeEndpoint(request.Endpoint!, request.Args)
                        ?? SocketAPIMessage.FromError("Endpoint not found.");

                    message.Id = request.Id;
                    SendResponse(client, message);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error handling client: {ex.Message}");
            }
            finally
            {
                clients.TryTake(out _);
            }
        }

        public void SendResponse(TcpClient client, SocketAPIMessage message)
        {
            message.Type = SocketAPIMessageType.Response;
            SendMessage(client, message);
        }

        public void SendEvent(TcpClient client, SocketAPIMessage message)
        {
            message.Type = SocketAPIMessageType.Event;
            SendMessage(client, message);
        }

        public async void BroadcastEvent(SocketAPIMessage message)
        {
            var sendTasks = clients.Where(client => client.Connected)
                                   .Select(client => Task.Run(() => SendEvent(client, message)));
            await Task.WhenAll(sendTasks);
        }

        private async void SendMessage(TcpClient toClient, SocketAPIMessage message)
        {
            byte[] wBuff = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);
            try
            {
                await toClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sending message to client: {ex.Message}");
                toClient.Close();
            }
        }

        public void Stop()
        {
            if (listener != null)
            {
                listener.Stop();
            }
            tcpListenerCancellationSource.Cancel();
        }

        private bool RegisterEndpoint(string name, Func<string, object?> handler)
        {
            if (apiEndpoints.ContainsKey(name))
                return false;

            apiEndpoints.Add(name, handler);
            return true;
        }

        private int RegisterEndpoints()
        {
            var endpoints = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Any())
                .SelectMany(c => c.GetMethods())
                .Where(m => m.GetCustomAttributes(typeof(SocketAPIEndpoint), true).Any())
                .Where(m => m.GetParameters().Length == 1 &&
                            m.IsStatic &&
                            m.GetParameters()[0].ParameterType == typeof(string) &&
                            m.ReturnType == typeof(object))
                .ToList();

            foreach (var endpoint in endpoints)
                RegisterEndpoint(endpoint.Name, (Func<string, object?>)endpoint.CreateDelegate(typeof(Func<string, object?>)));

            return endpoints.Count;
        }

        private SocketAPIMessage? InvokeEndpoint(string endpointName, string? jsonArgs)
        {
            if (!apiEndpoints.TryGetValue(endpointName, out var handler))
                return SocketAPIMessage.FromError("Endpoint not found.");

            try
            {
                object? rawResponse = handler.DynamicInvoke(jsonArgs);
                return SocketAPIMessage.FromValue(rawResponse);
            }
            catch (Exception ex)
            {
                return SocketAPIMessage.FromError(ex.InnerException?.Message ?? "An exception was thrown.");
            }
        }

        public void Dispose()
        {
            tcpListenerCancellationSource.Dispose();
            listener?.Stop();
            foreach (var client in clients)
                client?.Dispose();
        }
    }
}

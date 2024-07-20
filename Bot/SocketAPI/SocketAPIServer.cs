using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace SocketAPI
{
    /// <summary>
    /// Acts as an API server, accepting requests and replying over TCP/IP.
    /// </summary>
    public sealed class SocketAPIServer : IDisposable
    {
        private readonly CancellationTokenSource tcpListenerCancellationSource = new();
        private CancellationToken TcpListenerCancellationToken => tcpListenerCancellationSource.Token;
        private TcpListener? listener;
        private readonly Dictionary<string, Delegate> apiEndpoints = new();
        private readonly ConcurrentBag<TcpClient> clients = new();
        private static SocketAPIServer? _shared;

        private SocketAPIServer() { }

        public static SocketAPIServer Shared => _shared ??= new SocketAPIServer();

        public async Task Start(SocketAPIServerConfig config)
        {
            if (!config.Enabled) return;

            if (!config.LogsEnabled) Logger.DisableLogs();

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

            TcpListenerCancellationToken.Register(() => listener?.Stop());

            await AcceptClientsAsync();
        }

        private async Task AcceptClientsAsync()
        {
            while (!TcpListenerCancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await listener?.AcceptTcpClientAsync()!;
                    clients.Add(client);

                    var clientEP = client.Client.RemoteEndPoint as IPEndPoint;
                    Logger.LogInfo($"Client connected! IP: {clientEP?.Address}, Port: {clientEP?.Port}");

                    _ = HandleTcpClientAsync(client);
                }
                catch (OperationCanceledException) when (TcpListenerCancellationToken.IsCancellationRequested)
                {
                    Logger.LogInfo("Socket API server was closed.");
                    clients.Clear();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Error on the socket API server: {ex.Message}");
                }
            }
        }

        private async Task HandleTcpClientAsync(TcpClient client)
        {
            using var stream = client.GetStream();
            var buffer = new byte[client.ReceiveBufferSize];

            try
            {
                while (!TcpListenerCancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, TcpListenerCancellationToken);
                    if (bytesRead == 0)
                    {
                        Logger.LogInfo("Remote client closed the connection.");
                        break;
                    }

                    var rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    var request = SocketAPIProtocol.DecodeMessage(rawMessage);

                    if (request == null)
                    {
                        SendResponse(client, SocketAPIMessage.FromError("Error parsing request."));
                        continue;
                    }

                    var message = InvokeEndpoint(request.Endpoint!, request.Args)
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

        private void SendResponse(TcpClient client, SocketAPIMessage message)
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
            var wBuff = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);
            try
            {
                await toClient.GetStream().WriteAsync(wBuff, TcpListenerCancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sending message to client: {ex.Message}");
                toClient.Close();
            }
        }

        public void Stop()
        {
            listener?.Stop();
            tcpListenerCancellationSource.Cancel();
        }

        private bool RegisterEndpoint(string name, Func<string, object?> handler)
        {
            if (apiEndpoints.ContainsKey(name)) return false;

            apiEndpoints.Add(name, handler);
            return true;
        }

        private int RegisterEndpoints()
        {
            var endpoints = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Length != 0)
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
                var rawResponse = handler.DynamicInvoke(jsonArgs);
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

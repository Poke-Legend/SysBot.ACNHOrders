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
using SysBot.ACNHOrders.Bot.SocketAPI.Attributes;

namespace SocketAPI
{
    /// <summary>
    /// Acts as an API server, accepting requests and replying over TCP/IP.
    /// </summary>
    public sealed class SocketAPIServer
    {
        private readonly CancellationTokenSource _tcpListenerCancellationSource = new();
        private CancellationToken TcpListenerCancellationToken => _tcpListenerCancellationSource.Token;

        private TcpListener? _listener;
        private readonly Dictionary<string, Delegate> _apiEndpoints = new();
        private readonly ConcurrentBag<TcpClient> _clients = new();

        private static readonly Lazy<SocketAPIServer> _instance = new(() => new SocketAPIServer());
        public static SocketAPIServer Instance => _instance.Value;

        private SocketAPIServer() { }

        /// <summary>
        /// Starts listening for incoming connections on the configured port.
        /// </summary>
        public async Task Start(SocketAPIServerConfig config)
        {
            if (!config.Enabled) return;

            if (!config.LogsEnabled)
                Logger.DisableLogs();

            Logger.LogInfo($"Number of registered endpoints: {RegisterEndpoints()}");

            _listener = new TcpListener(IPAddress.Any, config.Port);

            try
            {
                _listener.Start();
                Logger.LogInfo($"Socket API server listening on port {config.Port}.");
            }
            catch (SocketException ex)
            {
                Logger.LogError($"Socket API server failed to start: {ex.Message}");
                return;
            }

            TcpListenerCancellationToken.Register(_listener.Stop);

            while (!TcpListenerCancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _clients.Add(client);

                    var clientEP = client.Client.RemoteEndPoint as IPEndPoint;
                    Logger.LogInfo($"Client connected! IP: {clientEP?.Address}, Port: {clientEP?.Port}");

                    HandleTcpClient(client);
                }
                catch (OperationCanceledException) when (TcpListenerCancellationToken.IsCancellationRequested)
                {
                    Logger.LogInfo("The socket API server was closed.");
                    ClearClients();
                }
                catch (Exception ex)
                {
                    Logger.LogError("An error occurred on the socket API server: " + ex.Message);

                }
            }
        }

        private async void HandleTcpClient(TcpClient client)
        {
            var stream = client.GetStream();
            var buffer = new byte[client.ReceiveBufferSize];

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, client.ReceiveBufferSize, TcpListenerCancellationToken);
                if (bytesRead == 0)
                {
                    Logger.LogInfo("Remote client closed the connection.");
                    break;
                }

                var rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                rawMessage = Regex.Replace(rawMessage, @"\r\n?|\n|\0", "");

                var request = SocketAPIProtocol.DecodeMessage(rawMessage);

                if (request == null)
                {
                    SendResponse(client, SocketAPIMessage.FromError("Error while JSON-parsing the request."));
                    continue;
                }

                var response = InvokeEndpoint(request.endpoint!, request.args) ??
                               SocketAPIMessage.FromError("Endpoint not found.");

                response.id = request.id;
                SendResponse(client, response);
            }
        }

        public void SendResponse(TcpClient client, SocketAPIMessage message)
        {
            message.type = SocketAPIMessageType.Response;
            SendMessage(client, message);
        }

        public void SendEvent(TcpClient client, SocketAPIMessage message)
        {
            message.type = SocketAPIMessageType.Event;
            SendMessage(client, message);
        }

        public async void BroadcastEvent(SocketAPIMessage message)
        {
            var tasks = _clients
                .Where(client => client.Connected)
                .Select(client => Task.Run(() => SendEvent(client, message)));

            await Task.WhenAll(tasks);
        }

        private async void SendMessage(TcpClient toClient, SocketAPIMessage message)
        {
            var buffer = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);

            try
            {
                await toClient.GetStream().WriteAsync(buffer, 0, buffer.Length, TcpListenerCancellationToken);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sending message to client: {ex.Message}");
                toClient.Close();
            }
        }

        public void Stop()
        {
            _listener?.Stop();
            _tcpListenerCancellationSource.Cancel();
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
                            m.ReturnType == typeof(object));

            foreach (var endpoint in endpoints)
            {
                RegisterEndpoint(endpoint.Name, (Func<string, object?>)endpoint.CreateDelegate(typeof(Func<string, object?>)));
            }

            return endpoints.Count();
        }

        private bool RegisterEndpoint(string name, Func<string, object?> handler)
        {
            if (_apiEndpoints.ContainsKey(name)) return false;
            _apiEndpoints[name] = handler;
            return true;
        }

        private SocketAPIMessage? InvokeEndpoint(string endpointName, string? jsonArgs)
        {
            if (!_apiEndpoints.TryGetValue(endpointName, out var handler))
                return SocketAPIMessage.FromError("Endpoint not found.");

            try
            {
                var response = handler.DynamicInvoke(jsonArgs);
                return SocketAPIMessage.FromValue(response);
            }
            catch (Exception ex)
            {
                return SocketAPIMessage.FromError(ex.InnerException?.Message ?? "Exception thrown during endpoint invocation.");
            }
        }

        private void ClearClients()
        {
            while (!_clients.IsEmpty)
            {
                _clients.TryTake(out _);
            }
        }
    }
}

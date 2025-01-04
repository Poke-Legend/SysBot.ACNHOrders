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
    public sealed class SocketAPIServer : IDisposable
    {
        private readonly CancellationTokenSource _tcpListenerCancellationSource = new();
        private CancellationToken TcpListenerCancellationToken => _tcpListenerCancellationSource.Token;

        private TcpListener? _listener;
        private readonly Dictionary<string, Delegate> _apiEndpoints = new();
        private readonly ConcurrentBag<TcpClient> _clients = new();

        // Lazy singleton pattern
        private static readonly Lazy<SocketAPIServer> _instance = new(() => new SocketAPIServer());
        public static SocketAPIServer Instance => _instance.Value;

        private Dictionary<string, Delegate>? _endpointCache;
        private const int BufferSize = 8192; // For read/write on the TCP stream

        /// <summary>
        /// Private constructor for singleton pattern.
        /// </summary>
        private SocketAPIServer() { }

        /// <summary>
        /// Starts listening for incoming connections on the specified config port, 
        /// spawns tasks to handle each client.
        /// </summary>
        public async Task Start(SocketAPIServerConfig config)
        {
            if (!config.Enabled) return;

            if (!config.LogsEnabled)
                Logger.DisableLogs();

            // Register all recognized endpoints
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

            // When canceled, stop the listener
            TcpListenerCancellationToken.Register(_listener.Stop);

            while (!TcpListenerCancellationToken.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _clients.Add(client);

                    var clientEP = client.Client.RemoteEndPoint as IPEndPoint;
                    Logger.LogInfo($"Client connected! IP: {clientEP?.Address}, Port: {clientEP?.Port}");

                    // Fire-and-forget client handler
                    _ = HandleTcpClient(client);
                }
                catch (OperationCanceledException) when (TcpListenerCancellationToken.IsCancellationRequested)
                {
                    Logger.LogInfo("The socket API server was closed via cancellation.");
                    ClearClients();
                }
                catch (Exception ex)
                {
                    Logger.LogError("An error occurred on the socket API server: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// For each connected client, read messages, parse them, and invoke the appropriate endpoint.
        /// </summary>
        private async Task HandleTcpClient(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[BufferSize];

                while (!TcpListenerCancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, TcpListenerCancellationToken)
                                                .ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        Logger.LogInfo("Remote client closed the connection.");
                        break;
                    }

                    // Convert bytes to string, parse as request
                    var rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    var request = SocketAPIProtocol.DecodeMessage(rawMessage);

                    if (request == null)
                    {
                        await SendResponse(client, SocketAPIMessage.FromError("Error while JSON-parsing the request."));
                        continue;
                    }

                    var response = InvokeEndpoint(request.Endpoint!, request.Args)
                                   ?? SocketAPIMessage.FromError("Endpoint not found.");
                    response.Id = request.Id;

                    await SendResponse(client, response);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in handling client: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a direct response (type = Response) to the client.
        /// </summary>
        public async Task SendResponse(TcpClient client, SocketAPIMessage message)
        {
            message.Type = SocketAPIMessageType.Response;
            await SendMessage(client, message).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an event (type = Event) to the client.
        /// </summary>
        public async Task SendEvent(TcpClient client, SocketAPIMessage message)
        {
            message.Type = SocketAPIMessageType.Event;
            await SendMessage(client, message).ConfigureAwait(false);
        }

        /// <summary>
        /// Broadcasts an event to all currently connected clients.
        /// </summary>
        public async Task BroadcastEvent(SocketAPIMessage message)
        {
            var tasks = _clients
                .Where(client => client.Connected)
                .Select(client => SendEvent(client, message));

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// Encodes and sends a SocketAPIMessage to a specific client.
        /// </summary>
        private async Task SendMessage(TcpClient toClient, SocketAPIMessage message)
        {
            var encodedMsg = SocketAPIProtocol.EncodeMessage(message);
            if (encodedMsg == null)
                return;

            var buffer = Encoding.UTF8.GetBytes(encodedMsg);

            try
            {
                await toClient.GetStream().WriteAsync(buffer, 0, buffer.Length, TcpListenerCancellationToken)
                              .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error sending message to client: {ex.Message}");
                toClient.Close();
            }
        }

        /// <summary>
        /// Stops the server: stops listening, cancels, and clears connected clients.
        /// </summary>
        public void Stop()
        {
            _listener?.Stop();
            _tcpListenerCancellationSource.Cancel();
            ClearClients();
        }

        /// <summary>
        /// Registers endpoints by scanning assemblies for [SocketAPIController] & [SocketAPIEndpoint].
        /// </summary>
        private int RegisterEndpoints()
        {
            // If we've already scanned assemblies
            if (_endpointCache != null)
            {
                _apiEndpoints.Clear();
                foreach (var endpoint in _endpointCache)
                    _apiEndpoints[endpoint.Key] = endpoint.Value;
                return _apiEndpoints.Count;
            }

            var endpoints = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
                .SelectMany(a => a.GetTypes())
                .Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Any())
                .SelectMany(c => c.GetMethods())
                .Where(m => m.GetCustomAttributes(typeof(SocketAPIEndpoint), true).Any())
                .Where(m =>
                    m.GetParameters().Length == 1 &&
                    m.IsStatic &&
                    m.GetParameters()[0].ParameterType == typeof(string) &&
                    m.ReturnType == typeof(object)
                )
                .ToDictionary(
                    m => m.Name,
                    m => (Delegate)m.CreateDelegate(typeof(Func<string, object?>))
                );

            _apiEndpoints.Clear();
            foreach (var endpoint in endpoints)
                _apiEndpoints[endpoint.Key] = endpoint.Value;

            _endpointCache = _apiEndpoints;

            return _apiEndpoints.Count;
        }

        /// <summary>
        /// Invokes the specified endpoint by name, passing jsonArgs to it.
        /// Returns a SocketAPIMessage with the result or an error.
        /// </summary>
        private SocketAPIMessage? InvokeEndpoint(string endpointName, string? jsonArgs)
        {
            if (!_apiEndpoints.TryGetValue(endpointName, out var handler))
                return SocketAPIMessage.FromError("Endpoint not found.");

            try
            {
                var response = ((Func<string, object?>)handler)(jsonArgs ?? string.Empty);
                return SocketAPIMessage.FromValue(response);
            }
            catch (Exception ex)
            {
                return SocketAPIMessage.FromError(ex.Message ?? "Unknown error");
            }
        }

        /// <summary>
        /// Closes and removes all connected clients from the list.
        /// </summary>
        private void ClearClients()
        {
            while (!_clients.IsEmpty)
            {
                if (_clients.TryTake(out var client))
                {
                    client?.Close();
                }
            }
        }

        /// <summary>
        /// Disposes of resources by stopping the server and disposing clients & cancellation token.
        /// </summary>
        public void Dispose()
        {
            Stop();
            foreach (var client in _clients)
            {
                client?.Dispose();
            }
            _tcpListenerCancellationSource.Dispose();
        }
    }
}

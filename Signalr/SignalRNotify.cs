using Microsoft.AspNetCore.SignalR.Client;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Signalr
{
    public class SignalRNotify : IDodoRestoreNotifier
    {
        private HubConnection Connection { get; }
        private string AuthID { get; }
        private string AuthString { get; }
        private string URI { get; }
        private bool Connected { get; set; }

        private static readonly SemaphoreSlim _asyncLock = new SemaphoreSlim(1, 1);

        public SignalRNotify(string authid, string authString, string uriEndpoint)
        {
            AuthID = authid ?? throw new ArgumentNullException(nameof(authid));
            AuthString = authString ?? throw new ArgumentNullException(nameof(authString));
            URI = uriEndpoint ?? throw new ArgumentNullException(nameof(uriEndpoint));

            Connection = new HubConnectionBuilder()
                .WithUrl(URI)
                .WithAutomaticReconnect()
                .Build();

            Task.Run(() => AttemptConnectionAsync());
        }

        private async Task AttemptConnectionAsync()
        {
            while (!Connected)
            {
                try
                {
                    await Connection.StartAsync();
                    LogUtil.LogInfo($"Connected successfully. ConnectionId: {Connection.ConnectionId}", "SignalR");
                    Connected = true;
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Connection failed: {ex.Message}", "SignalR");
                    await Task.Delay(5000); // Retry after 5 seconds if the connection fails
                }
            }
        }

        public void NotifyServerOfState(GameState gs)
        {
            var paramsToSend = new Dictionary<string, string>
            {
                { "gs", gs.ToString().WebSafeBase64Encode() }
            };

            Task.Run(() => NotifyServerEndpointAsync(paramsToSend));
        }

        public void NotifyServerOfDodoCode(string dodo)
        {
            var paramsToSend = new Dictionary<string, string>
            {
                { "dodo", dodo.WebSafeBase64Encode() }
            };

            Task.Run(() => NotifyServerEndpointAsync(paramsToSend));
        }

        private async Task NotifyServerEndpointAsync(Dictionary<string, string> urlParams)
        {
            var authToken = $"&{AuthID}={AuthString}";
            var uriTry = EncodeUriParams(URI, urlParams) + authToken;

            await _asyncLock.WaitAsync();
            try
            {
                await Connection.InvokeAsync("ReceiveViewMessage", AuthString, uriTry);
            }
            catch (Exception e)
            {
                LogUtil.LogError(e.Message, "SignalR");
            }
            finally
            {
                _asyncLock.Release();
            }
        }

        private string EncodeUriParams(string uriBase, Dictionary<string, string> urlParams)
        {
            if (urlParams.Count == 0)
                return uriBase;

            var sb = new StringBuilder(uriBase);
            if (!uriBase.EndsWith("?"))
            {
                sb.Append("?");
            }

            foreach (var kvp in urlParams)
            {
                sb.AppendFormat("{0}={1}&", kvp.Key, kvp.Value);
            }

            // Remove trailing '&'
            sb.Length -= 1;

            return sb.ToString();
        }
    }
}

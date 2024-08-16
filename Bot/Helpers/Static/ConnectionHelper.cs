using Discord.WebSocket;
using Discord;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Communication.Interfaces;

namespace SysBot.ACNHOrders
{
    public static class ConnectionHelper
    {
        private static DiscordSocketClient? _client;
        private static ISwitchConnectionAsync? _switchConnection;

        public const int MapChunkCount = 64;

        public static async Task<string> GetVersionAsync(this ISwitchConnectionAsync connection, CancellationToken token)
        {
            var gvbytes = Encoding.ASCII.GetBytes("getVersion\r\n");
            byte[] socketReturn = await connection.ReadRaw(gvbytes, 9, token).ConfigureAwait(false);
            string version = Encoding.UTF8.GetString(socketReturn).TrimEnd('\0').TrimEnd('\n');
            return version;
        }
        public static async Task<DiscordSocketClient> InitializeDiscordClientAsync(string token)
        {
            if (_client != null)
                return _client;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers,
                AlwaysDownloadUsers = true,
            });

            _client.Log += LogAsync;
            _client.Disconnected += async (exception) =>
            {
                
                await ReconnectAsync();
            };

            await _client.LoginAsync(TokenType.Bot, token);
            await _client.StartAsync();
            return _client;
        }
        private static async Task ReconnectAsync()
        {
            int delay = 10000; // Delay before attempting to reconnect
            while (true)
            {
                try
                {
                    await _client!.StartAsync(); // Ensure _client is not null
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Reconnect failed: {ex.Message}. Retrying in {delay / 1000} seconds.");
                    await Task.Delay(delay);
                }
            }
        }
        public static async Task<ISwitchConnectionAsync> InitializeSwitchConnectionAsync(ISwitchConnectionAsync connection)
        {
            _switchConnection = connection;

            await ConnectSwitchAsync();
            return _switchConnection;
        }

        private static Task LogAsync(LogMessage logMessage)
        {
            Console.WriteLine(logMessage.ToString());
            return Task.CompletedTask;
        }

        private static async Task<bool> ConnectSwitchAsync()
        {
            int delay = 10000; // Delay before attempting to reconnect
            while (true)
            {
                try
                {
                    _ = _switchConnection!.Connected; // Ensure _switchConnection is not null
                    Console.WriteLine("Switch connected successfully.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Switch connection failed: {ex.Message}. Retrying in {delay / 1000} seconds.");
                    await Task.Delay(delay);
                }
            }

            return false;
        }

        public static async Task<int> GetChargePercentAsync(this ISwitchConnectionAsync connection, CancellationToken token)
        {
            var gvbytes = Encoding.ASCII.GetBytes("charge\r\n");
            byte[] socketReturn = await connection.ReadRaw(gvbytes, 9, token).ConfigureAwait(false);
            string chargepc = Encoding.UTF8.GetString(socketReturn).TrimEnd('\0').TrimEnd('\n');
            return int.Parse(chargepc);
        }
        private static T[] SubArray<T>(T[] data, int index, int length)
        {
            if (index + length > data.Length)
                length = data.Length - index;
            T[] result = new T[length];
            Array.Copy(data, index, result, 0, length);
            return result;
        }

        // freeze
        public static async Task SetFreezePauseState(this ISwitchConnectionAsync connection, bool pause, CancellationToken token)
        {
            var cmd = Encoding.ASCII.GetBytes(pause ? "freezePause\r\n" : "freezeUnpause\r\n");
            await connection.SendRaw(cmd, token).ConfigureAwait(false);
        }

        public static async Task FreezeValues(this ISwitchConnectionAsync connection, uint offset, byte[] data, int chunkCount, CancellationToken token, bool unfreeze = false)
        {
            var chunkSize = data.Length / chunkCount;
            uint[] offsets = GetUnsafeOffsetsByChunkCount(offset, (uint)data.Length, (uint)chunkCount);
            var chunks = new List<byte[]>();

            for (int i = 0; i < chunkCount; ++i)
            {
                var toSend = SubArray(data, i * chunkSize, chunkSize);
                var cmd = unfreeze ? Encoding.ASCII.GetBytes($"unFreeze 0x{offsets[i]:X8}\r\n") : Encoding.ASCII.GetBytes($"freeze 0x{offsets[i]:X8} 0x{string.Concat(toSend.Select(z => $"{z:X2}"))}\r\n");
                await connection.SendRaw(cmd, token).ConfigureAwait(false);
            }
        }

        private static uint[] GetUnsafeOffsetsByChunkCount(uint startOffset, uint size, uint chunkCount)
        {
            var offsets = new uint[chunkCount];
            var chunkSize = size / chunkCount;
            for (uint i = 0; i < chunkCount; ++i)
                offsets[i] = startOffset + (i * chunkSize);
            return offsets;
        }
    }
}
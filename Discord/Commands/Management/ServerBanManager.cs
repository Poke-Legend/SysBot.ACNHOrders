using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public static class ServerBanManager
    {
        private static readonly HashSet<string> BannedServerIds = new();
        private static readonly SemaphoreSlim FileLock = new(1, 1);

        private const string GitHubRawServerBanUrl = "";
        private const string GitHubApiServerBanUrl = ";
        private const string GitHubToken = ""; // Replace with your GitHub token

        public static async Task InitializeAsync() => await LoadBannedServers();

        public static bool IsServerBanned(string serverId) => BannedServerIds.Contains(serverId);

        public static async Task BanServerAsync(string serverId)
        {
            lock (BannedServerIds)
            {
                if (!BannedServerIds.Contains(serverId))
                    BannedServerIds.Add(serverId);
            }
            await SaveBannedServers();
        }

        public static async Task UnbanServerAsync(string serverId)
        {
            lock (BannedServerIds)
            {
                BannedServerIds.Remove(serverId);
            }
            await SaveBannedServers();
        }

        private static async Task LoadBannedServers()
        {
            await FileLock.WaitAsync();
            try
            {
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");
                var response = await client.GetStringAsync(GitHubRawServerBanUrl);

                lock (BannedServerIds)
                {
                    BannedServerIds.Clear();
                    BannedServerIds.UnionWith(JsonSerializer.Deserialize<List<string>>(response) ?? new List<string>());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned servers: {ex.Message}");
            }
            finally
            {
                FileLock.Release();
            }
        }

        private static async Task SaveBannedServers()
        {
            await FileLock.WaitAsync();
            try
            {
                var jsonData = JsonSerializer.Serialize(BannedServerIds);
                var sha = await GetCurrentFileSha(GitHubApiServerBanUrl);

                var content = new StringContent(JsonSerializer.Serialize(new
                {
                    message = "Update server ban list",
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData)),
                    sha = sha
                }), Encoding.UTF8, "application/json");

                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");

                var response = await client.PutAsync(GitHubApiServerBanUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Failed to save server ban list. Status: {response.StatusCode}");
                }
            }
            finally
            {
                FileLock.Release();
            }
        }

        private static async Task<string?> GetCurrentFileSha(string apiUrl)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");

            var response = await client.GetStringAsync(apiUrl);
            var fileInfo = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
            return fileInfo != null && fileInfo.TryGetValue("sha", out var sha) ? sha.ToString() : null;
        }
    }
}

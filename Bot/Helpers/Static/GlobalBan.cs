using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class GlobalBan
    {
        // Cache for banned users and servers
        private static readonly List<string> BannedUsers = new();
        private static readonly List<string> BannedServers = new();

        // Semaphores for thread safety
        private static readonly SemaphoreSlim UserSemaphore = new(1, 1);
        private static readonly SemaphoreSlim ServerSemaphore = new(1, 1);

        // GitHub API URLs and token
        private const string GitHubRawUserBanUrl = "https://raw.githubusercontent.com/Poke-Legend/ACNH-DATABASE/main/userban.json";
        private const string GitHubApiUserBanUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/userban.json";
        private const string GitHubRawServerBanUrl = "https://raw.githubusercontent.com/Poke-Legend/ACNH-DATABASE/main/serverban.json";
        private const string GitHubApiServerBanUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/serverban.json";
        private const string GitHubToken = "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"; // Replace with your GitHub token

        private static Timer? _banCacheTimer;

        // Initialize and start periodic cache refresh
        public static void InitializeBanCache()
        {
            // Load data from GitHub on startup
            _ = LoadBannedUsers();
            _ = LoadBannedServers();
            _banCacheTimer = new Timer(async _ => await RefreshBanCache(), null, TimeSpan.Zero, TimeSpan.FromMinutes(10));
        }

        private static async Task RefreshBanCache()
        {
            Console.WriteLine("Refreshing ban cache...");
            await LoadBannedUsers();
            await LoadBannedServers();
        }

        public static bool IsUserBannedAsync(string userId)
        {
            lock (BannedUsers)
            {
                return BannedUsers.Contains(userId);
            }
        }

        public static bool IsServerBannedAsync(string serverId)
        {
            lock (BannedServers)
            {
                return BannedServers.Contains(serverId);
            }
        }

        public static async Task BanUserAsync(string userId)
        {
            lock (BannedUsers)
            {
                if (!BannedUsers.Contains(userId))
                    BannedUsers.Add(userId);
            }
            await SaveBannedUsers();
        }

        public static async Task UnbanUserAsync(string userId)
        {
            lock (BannedUsers)
            {
                BannedUsers.Remove(userId);
            }
            await SaveBannedUsers();
        }

        public static async Task BanServerAsync(string serverId)
        {
            lock (BannedServers)
            {
                if (!BannedServers.Contains(serverId))
                    BannedServers.Add(serverId);
            }
            await SaveBannedServers();
        }

        public static async Task UnbanServerAsync(string serverId)
        {
            lock (BannedServers)
            {
                BannedServers.Remove(serverId);
            }
            await SaveBannedServers();
        }

        private static async Task LoadBannedUsers()
        {
            await UserSemaphore.WaitAsync();
            try
            {
                Console.WriteLine("Attempting to load banned users from GitHub...");
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");

                var response = await client.GetStringAsync(GitHubRawUserBanUrl);
                if (!string.IsNullOrEmpty(response))
                {
                    var bannedUsers = JsonSerializer.Deserialize<List<string>>(response);
                    if (bannedUsers != null)
                    {
                        BannedUsers.Clear();
                        BannedUsers.AddRange(bannedUsers);
                        Console.WriteLine("Successfully loaded banned users from GitHub.");
                    }
                    else
                    {
                        Console.WriteLine("Failed to deserialize banned users from GitHub response.");
                    }
                }
                else
                {
                    Console.WriteLine("GitHub response for banned users was empty.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned users from GitHub: {ex.Message}");
            }
            finally
            {
                UserSemaphore.Release();
            }
        }

        private static async Task LoadBannedServers()
        {
            await ServerSemaphore.WaitAsync();
            try
            {
                Console.WriteLine("Attempting to load banned servers from GitHub...");
                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");

                var response = await client.GetStringAsync(GitHubRawServerBanUrl);
                if (!string.IsNullOrEmpty(response))
                {
                    var bannedServers = JsonSerializer.Deserialize<List<string>>(response);
                    if (bannedServers != null)
                    {
                        BannedServers.Clear();
                        BannedServers.AddRange(bannedServers);
                        Console.WriteLine("Successfully loaded banned servers from GitHub.");
                    }
                    else
                    {
                        Console.WriteLine("Failed to deserialize banned servers from GitHub response.");
                    }
                }
                else
                {
                    Console.WriteLine("GitHub response for banned servers was empty.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned servers from GitHub: {ex.Message}");
            }
            finally
            {
                ServerSemaphore.Release();
            }
        }

        private static async Task SaveBannedUsers()
        {
            await SaveBanList(GitHubApiUserBanUrl, BannedUsers, UserSemaphore);
        }

        private static async Task SaveBannedServers()
        {
            await SaveBanList(GitHubApiServerBanUrl, BannedServers, ServerSemaphore);
        }

        private static async Task SaveBanList(string apiUrl, List<string> banList, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                string jsonData = JsonSerializer.Serialize(banList);
                string? sha = await GetCurrentFileSha(apiUrl);

                if (sha == null)
                {
                    Console.WriteLine("Failed to retrieve SHA for the file. Save operation aborted.");
                    return;
                }

                var content = new StringContent(JsonSerializer.Serialize(new
                {
                    message = "Update ban list",
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData)),
                    sha = sha
                }), Encoding.UTF8, "application/json");

                using HttpClient client = new();
                client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");

                var response = await client.PutAsync(apiUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to save ban list to GitHub. Status: {response.StatusCode}, Response: {responseBody}");
                }
                else
                {
                    Console.WriteLine("Ban list successfully updated on GitHub.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving ban list to GitHub: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        }

        private static async Task<string?> GetCurrentFileSha(string apiUrl)
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.Add("User-Agent", "ACNHOrdersBot");
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {GitHubToken}");

            try
            {
                var response = await client.GetStringAsync(apiUrl);
                var fileInfo = JsonSerializer.Deserialize<Dictionary<string, object>>(response);
                if (fileInfo != null && fileInfo.TryGetValue("sha", out var sha))
                {
                    return sha?.ToString();
                }
                Console.WriteLine("Could not retrieve SHA from GitHub response.");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting SHA: {ex.Message}");
                return null;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public static class BanManager
    {
        private static readonly HashSet<string> BannedUserIds = new();
        private static readonly SemaphoreSlim UserFileLock = new(1, 1);

        private static readonly HashSet<string> BannedServerIds = new();
        private static readonly SemaphoreSlim ServerFileLock = new(1, 1);

        public static async Task InitializeAsync()
        {
            await LoadBannedUsers();
            await LoadBannedServers();
        }

        public static bool IsUserBanned(string userId)
        {
            lock (BannedUserIds)
            {
                return BannedUserIds.Contains(userId);
            }
        }

        public static bool IsServerBanned(string serverId)
        {
            lock (BannedServerIds)
            {
                return BannedServerIds.Contains(serverId);
            }
        }

        public static async Task BanServerAsync(string serverId)
        {
            lock (BannedServerIds)
            {
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

        private static async Task LoadBannedUsers()
        {
            await UserFileLock.WaitAsync();
            try
            {
                using var client = GitHubApi.CreateHttpClient();
                var response = await client.GetStringAsync(GitHubApi.UserBanApiUrl);

                var fileData = JsonSerializer.Deserialize<GitHubFileResponse>(response);

                if (fileData?.Content != null)
                {
                    var decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Content));
                    lock (BannedUserIds)
                    {
                        BannedUserIds.Clear();
                        BannedUserIds.UnionWith(JsonSerializer.Deserialize<List<string>>(decodedContent) ?? new List<string>());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned users: {ex.Message}");
            }
            finally
            {
                UserFileLock.Release();
            }
        }

        private static async Task LoadBannedServers()
        {
            await ServerFileLock.WaitAsync();
            try
            {
                using var client = GitHubApi.CreateHttpClient();
                var response = await client.GetStringAsync(GitHubApi.ServerBanApiUrl);

                var fileData = JsonSerializer.Deserialize<GitHubFileResponse>(response);

                if (fileData?.Content != null)
                {
                    var decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Content));
                    lock (BannedServerIds)
                    {
                        BannedServerIds.Clear();
                        BannedServerIds.UnionWith(JsonSerializer.Deserialize<List<string>>(decodedContent) ?? new List<string>());
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading banned servers: {ex.Message}");
            }
            finally
            {
                ServerFileLock.Release();
            }
        }

        private static async Task SaveBannedServers()
        {
            await SaveBanList(GitHubApi.ServerBanApiUrl, BannedServerIds, ServerFileLock, "Update server ban list");
        }

        private static async Task SaveBannedUsers()
        {
            await SaveBanList(GitHubApi.UserBanApiUrl, BannedUserIds, UserFileLock, "Update user ban list");
        }

        private static async Task SaveBanList(string apiUrl, HashSet<string> banList, SemaphoreSlim fileLock, string commitMessage)
        {
            await fileLock.WaitAsync();
            try
            {
                var jsonData = JsonSerializer.Serialize(banList);
                string? sha = await GitHubApi.GetFileShaAsync(apiUrl);

                bool success = await GitHubApi.UpdateFileAsync(jsonData, commitMessage, apiUrl, sha);

                if (!success)
                {
                    Console.WriteLine("Failed to save the ban list to GitHub.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during API request: {ex.Message}");
            }
            finally
            {
                fileLock.Release();
            }
        }
    }
}

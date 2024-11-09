using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public class ChannelManager
    {
        private static readonly HashSet<ulong> ChannelIds = new();
        private static readonly SemaphoreSlim ChannelFileLock = new(1, 1);

        public static async Task InitializeAsync()
        {
            await LoadChannelsAsync();
        }

        // Adds a channel to the ChannelIds collection and saves it to GitHub
        public static async Task<bool> AddChannelAsync(ulong channelId)
        {
            lock (ChannelIds)
            {
                ChannelIds.Add(channelId);
            }
            Console.WriteLine($"Channel {channelId} added locally. Now saving to GitHub...");
            return await SaveChannelsAsync();
        }

        // Removes a channel from the ChannelIds collection and saves it to GitHub
        public static async Task<bool> RemoveChannelAsync(ulong channelId)
        {
            lock (ChannelIds)
            {
                ChannelIds.Remove(channelId);
            }
            Console.WriteLine($"Channel {channelId} removed locally. Now saving to GitHub...");
            return await SaveChannelsAsync();
        }

        public static async Task<List<ulong>> LoadChannelsAsync()
        {
            await ChannelFileLock.WaitAsync();
            try
            {
                using var client = GitHubApi.CreateHttpClient();
                var response = await client.GetStringAsync(GitHubApi.ChannelListApiUrl);

                var fileData = JsonSerializer.Deserialize<GitHubFileResponse>(response);

                if (fileData?.Content != null)
                {
                    var decodedContent = Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Content));
                    lock (ChannelIds)
                    {
                        ChannelIds.Clear();
                        ChannelIds.UnionWith(JsonSerializer.Deserialize<List<ulong>>(decodedContent) ?? new List<ulong>());
                    }
                    Console.WriteLine("Channels loaded successfully from GitHub.");
                }
                else
                {
                    Console.WriteLine("Warning: No content found in GitHub file response.");
                }

                return new List<ulong>(ChannelIds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading channels: {ex.Message}");
                return new List<ulong>();
            }
            finally
            {
                ChannelFileLock.Release();
            }
        }

        // Updated SaveChannelsAsync to return Task<bool>
        public static async Task<bool> SaveChannelsAsync()
        {
            await ChannelFileLock.WaitAsync();
            try
            {
                var jsonData = JsonSerializer.Serialize(ChannelIds);
                string commitMessage = "Update channel list";
                string apiUrl = GitHubApi.ChannelListApiUrl;

                string? sha = await GitHubApi.GetFileShaAsync(apiUrl);
                Console.WriteLine($"Retrieved SHA for GitHub file: {sha}");

                bool success = await GitHubApi.UpdateFileAsync(jsonData, commitMessage, apiUrl, sha);

                if (success)
                {
                    Console.WriteLine("Channel list saved successfully to GitHub.");
                }
                else
                {
                    Console.WriteLine("Failed to save the channel list to GitHub.");
                }

                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during API request: {ex.Message}");
                return false;
            }
            finally
            {
                ChannelFileLock.Release();
            }
        }
    }
}

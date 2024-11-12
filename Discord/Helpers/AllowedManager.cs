using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public static class AllowedManager
    {
        private static IReadOnlyList<ulong> _cachedBlacklist = new List<ulong>(); // Read-only list
        private static DateTime _lastFetchTime = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(1); // 1-minute cache

        public static async Task<bool> ServerBlacklisted(ulong? guildId = null)
        {
            // Early exit if guildId is null or 0
            if (!guildId.HasValue || guildId == 0) return false;

            await EnsureBlacklistUpdated();

            // Check if the guildId is in the read-only blacklist (return true if blacklisted)
            return _cachedBlacklist.Contains(guildId.Value);
        }

        private static async Task EnsureBlacklistUpdated()
        {
            // If cache is still valid, skip re-fetching to prevent excessive requests
            if (DateTime.UtcNow - _lastFetchTime < _cacheDuration) return;

            try
            {
                using var client = new HttpClient();
                var url = "https://sysbots.net/DATABASE/ANCH/ServerBanned.json";

                // Fetch the blacklist from the provided URL
                var response = await client.GetStringAsync(url);

                // Parse the JSON response as a list of strings, then safely convert each to ulong
                var parsedList = JArray.Parse(response)
                    .Select(x => ulong.TryParse((string?)x, out var id) ? id : (ulong?)null)
                    .Where(id => id != null)
                    .Select(id => id!.Value)
                    .ToList()
                    .AsReadOnly();

                _cachedBlacklist = parsedList;
                _lastFetchTime = DateTime.UtcNow; // Update fetch time
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching or parsing blacklist: {ex.Message}");
                // Retain the previous read-only blacklist if an error occurs
            }
        }
    }
}

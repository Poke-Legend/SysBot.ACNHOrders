using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using SysBot.ACNHOrders.Discord.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class Broadcast : ModuleBase<SocketCommandContext>
    {
        private class Config
        {
            public List<ulong> Channels { get; set; } = new List<ulong>();
            public List<ulong> Sudo { get; set; } = new List<ulong>();
        }

        private async Task<Config> LoadConfigAsync()
        {
            try
            {
                var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
                if (string.IsNullOrEmpty(channelListJson))
                {
                    Console.WriteLine("Failed to fetch channel list from GitHub.");
                    return new Config();
                }

                // Deserialize directly into a list of ulong for channel IDs
                var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson);
                if (channelIds == null)
                {
                    Console.WriteLine("Failed to deserialize channel list.");
                    return new Config();
                }

                // Initialize a Config object and assign the deserialized channel IDs
                var config = new Config
                {
                    Channels = channelIds
                };

                // Optionally, fetch Sudo user IDs from another source or keep it empty as default
                var sudoJson = await GitHubApi.FetchFileContentAsync(GitHubApi.SudoApiUrl);
                if (!string.IsNullOrEmpty(sudoJson))
                {
                    config.Sudo = JsonConvert.DeserializeObject<List<ulong>>(sudoJson) ?? new List<ulong>();
                }

                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config from GitHub: {ex.Message}");
                return new Config();
            }
        }


        private bool IsUserAuthorized(Config config)
        {
            return config.Sudo.Contains(Context.User.Id);
        }

        private async Task BroadcastMessageAsync(IMessageChannel channel, string message)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Announcement")
                .WithDescription(message)
                .WithThumbnailUrl("https://media.giphy.com/media/T87BZ7cyOH7TwDBgwy/giphy.gif")
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }

        [Command("broadcast")]
        [Summary("Broadcasts a message to channels specified in the GitHub config")]
        [RequireSudo]
        public async Task BroadcastAsync([Remainder] string message)
        {
            if (Context.Client == null)
            {
                await ReplyAsync("Client is not initialized.");
                return;
            }

            var config = await LoadConfigAsync();
            if (config == null)
            {
                await ReplyAsync("Configuration error, please ensure GitHub configuration is properly set up.");
                return;
            }

            if (!IsUserAuthorized(config))
            {
                await ReplyAsync("You are not authorized to use this command.");
                return;
            }

            foreach (var channelId in config.Channels)
            {
                var channel = Context.Client.GetChannel(channelId) as IMessageChannel;
                if (channel == null)
                {
                    Console.WriteLine($"Channel with ID {channelId} not found.");
                    continue;
                }

                if (Context.Channel is IGuildChannel guildChannel)
                {
                    var botPermissions = Context.Guild?.CurrentUser?.GetPermissions(guildChannel);
                    if (botPermissions?.SendMessages != true)
                    {
                        Console.WriteLine($"Bot lacks permission to send messages in channel ID {channelId}.");
                        continue;
                    }
                }

                await BroadcastMessageAsync(channel, message);
            }
        }
    }
}

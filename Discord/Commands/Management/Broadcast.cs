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

        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

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

                var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson);
                if (channelIds == null)
                {
                    Console.WriteLine("Failed to deserialize channel list.");
                    return new Config();
                }

                var config = new Config
                {
                    Channels = channelIds
                };

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
                .WithTitle("?? Server Announcement")
                .WithDescription(message)
                .WithColor(EmbedColor)
                .WithThumbnailUrl("https://media.giphy.com/media/T87BZ7cyOH7TwDBgwy/giphy.gif")
                .WithFooter($"Sent by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithTimestamp(DateTimeOffset.Now)
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
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("? Client Not Initialized")
                    .WithDescription("The Discord client is not initialized. Please try again later.")
                    .Build();
                await ReplyAsync(embed: errorEmbed);
                return;
            }

            var config = await LoadConfigAsync();
            if (config == null)
            {
                var configErrorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("? Configuration Error")
                    .WithDescription("There was an issue loading the configuration. Please ensure GitHub configuration is properly set up.")
                    .Build();
                await ReplyAsync(embed: configErrorEmbed);
                return;
            }

            if (!IsUserAuthorized(config))
            {
                var unauthorizedEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("?? Unauthorized")
                    .WithDescription("You do not have the necessary permissions to use this command.")
                    .Build();
                await ReplyAsync(embed: unauthorizedEmbed);
                return;
            }

            int successfulBroadcasts = 0;
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
                successfulBroadcasts++;
            }

            var successEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("? Broadcast Complete")
                .WithDescription($"Broadcast message was sent to {successfulBroadcasts} channel(s).")
                .WithFooter("Broadcast finished", Context.Client.CurrentUser.GetAvatarUrl())
                .Build();

            await ReplyAsync(embed: successEmbed);
        }
    }
}

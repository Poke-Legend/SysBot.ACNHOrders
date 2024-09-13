using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
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

        private Config LoadConfig()
        {
            try
            {
                var configContent = File.ReadAllText("config.json");
                return JsonConvert.DeserializeObject<Config>(configContent) ?? new Config();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading config file: {ex.Message}");
                return new Config(); // Return a default config to avoid returning null
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
        [Summary("Broadcasts a message to channels specified in the config")]
        [RequireSudo]
        public async Task BroadcastAsync([Remainder] string message)
        {
            if (Context.Client == null)
            {
                await ReplyAsync("Client is not initialized.");
                return;
            }

            var config = LoadConfig();
            if (config == null)
            {
                await ReplyAsync("Configuration error, please ensure config.json is properly configured.");
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

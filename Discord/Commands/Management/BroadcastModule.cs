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
    public class BroadcastModule : ModuleBase<SocketCommandContext>
    {
        private class Config
        {
            public List<ulong> Channels { get; set; } = new List<ulong>();
            public List<ulong> Sudo { get; set; } = new List<ulong>();
        }

        [Command("broadcast")]
        [Summary("Broadcasts a message to channel ids in config")]
        [RequireSudo]
        public async Task BroadcastAsync([Remainder] string message)
        {
            if (Context.Client == null)
            {
                Console.WriteLine("Client is null");
                return;
            }

            var config = JsonConvert.DeserializeObject<Config>(File.ReadAllText("config.json"));
            if (config == null)
            {
                await ReplyAsync("Configuration error, please ensure config.json is properly configured.");
                return;
            }

            if (!config.Sudo.Contains(Context.User.Id))
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

                var embed = new EmbedBuilder()
                    .WithTitle("Announcement")
                    .WithDescription(message)
                    .WithThumbnailUrl("https://media.giphy.com/media/T87BZ7cyOH7TwDBgwy/giphy.gif")
                    .Build();

                await channel.SendMessageAsync(embed: embed);
            }
        }
    }
}

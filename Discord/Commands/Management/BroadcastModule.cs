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
            public Config()
            {
                Channels = new List<ulong>();
                Sudo = new List<ulong>();
            }

            public List<ulong> Channels { get; set; }
            public List<ulong> Sudo { get; set; }
        }

        [Command("broadcast")]
        [Summary("Broadcasts a message to channel ids in config")]
        [RequireSudo]
        public async Task BroadcastAsync([Remainder] string message)
        {
            if (Context?.Client == null)
            {
                Console.WriteLine("Context or Client is null");
                return;
            }

            var client = Context.Client;
            var configJson = File.ReadAllText("config.json");
            var config = JsonConvert.DeserializeObject<Config>(configJson);

            if (config == null)
            {
                await ReplyAsync("Configuration error, please ensure config.json is properly configured.");
                return;
            }

            if (Context?.User?.Id == null)
            {
                Console.WriteLine("Context or User or Id is null");
                return;
            }

            if (!config.Sudo.Contains(Context.User.Id))
            {
                await ReplyAsync("You are not authorized to use this command.");
                return;
            }

            foreach (var channelId in config.Channels)
            {
                var channel = client.GetChannel(channelId) as IMessageChannel;
                if (channel == null)
                {
                    Console.WriteLine($"Channel with ID {channelId} is null");
                    continue;
                }

                if (Context.Channel is IGuildChannel)
                {
                    if (Context?.Guild?.CurrentUser == null)
                    {
                        Console.WriteLine("Context or Guild or CurrentUser is null");
                        return;
                    }

                    var botPermission = (Context.Guild.CurrentUser as IGuildUser)?.GetPermissions(channel as IGuildChannel);
                    if (botPermission?.SendMessages != true)
                    {
                        continue; // If bot doesn't have permission, skip this channel
                    }
                }

                // Create an EmbedBuilder and construct the embedded message
                var embed = new EmbedBuilder();
                embed.WithTitle("Announcement");
                embed.WithDescription(message);
                embed.WithThumbnailUrl("https://media.giphy.com/media/T87BZ7cyOH7TwDBgwy/giphy.gif");
                var broadcastMessage = embed.Build();

                await channel.SendMessageAsync(null, false, broadcastMessage);
            }
        }
    }
}

using Discord.Commands;
using Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Color = System.Drawing.Color;
using DiscordColor = Discord.Color;
using Discord.WebSocket;
using System.Net.Http;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class OwnerModule : ModuleBase<SocketCommandContext>
    {
        [Command("listguilds")]
        [Alias("lg", "servers", "listservers")]
        [Summary("Lists all guilds the bot is part of.")]
        [RequireSudo]
        public async Task ListGuilds(int page = 1)
        {
            const int guildsPerPage = 25; // Discord limit for fields in an embed
            int guildCount = Context.Client.Guilds.Count;
            int totalPages = (int)Math.Ceiling(guildCount / (double)guildsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var guilds = Context.Client.Guilds
                .Skip((page - 1) * guildsPerPage)
                .Take(guildsPerPage);

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"List of Guilds - Page {page}/{totalPages}")
                .WithDescription("Here are the guilds I'm currently in:")
                .WithColor((DiscordColor)Color.Blue);

            foreach (var guild in guilds)
            {
                embedBuilder.AddField(guild.Name, $"ID: {guild.Id}", inline: true);
            }
            var dmChannel = await Context.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embedBuilder.Build());

            await ReplyAsync($"{Context.User.Mention}, I've sent you a DM with the list of guilds (Page {page}).");

            if (Context.Message is IUserMessage userMessage)
            {
                await Task.Delay(2000);
                await userMessage.DeleteAsync().ConfigureAwait(false);
            }
        }
        
        [Command("leave")]
        [Alias("bye")]
        [Summary("Leaves the current server.")]
        [RequireOwner]
        public async Task Leave()
        {
            await ReplyAsync("Goodbye.").ConfigureAwait(false);
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }

        [Command("leaveguild")]
        [Alias("lg")]
        [Summary("Leaves guild based on supplied ID.")]
        [RequireOwner]
        public async Task LeaveGuild(string userInput)
        {
            if (!ulong.TryParse(userInput, out ulong id))
            {
                await ReplyAsync("Please provide a valid Guild ID.").ConfigureAwait(false);
                return;
            }

            var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
            if (guild is null)
            {
                await ReplyAsync($"Provided input ({userInput}) is not a valid guild ID or the bot is not in the specified guild.").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"Leaving {guild}.").ConfigureAwait(false);
            await guild.LeaveAsync().ConfigureAwait(false);
        }

        [Command("leaveall")]
        [Summary("Leaves all servers the bot is currently in.")]
        [RequireOwner]
        public async Task LeaveAll()
        {
            await ReplyAsync("Leaving all servers.").ConfigureAwait(false);
            foreach (var guild in Context.Client.Guilds)
            {
                await guild.LeaveAsync().ConfigureAwait(false);
            }
        }
        [Command("dm")]
        [Summary("Sends a direct message to a specified user.")]
        [RequireOwner]
        public async Task DMUserAsync(SocketUser user, [Remainder] string message)
        {
            var attachments = Context.Message.Attachments;
            var hasAttachments = attachments.Count != 0;

            var embed = new EmbedBuilder
            {
                Title = "Private Message from the Bot Owner",
                Description = message,
                Color = (DiscordColor?)Color.Gold,
                Timestamp = DateTimeOffset.Now,
                ThumbnailUrl = "https://raw.githubusercontent.com/Poke-Legend/ACNH-Images/main/Images/Mail/MailBox.png"
            };

            try
            {
                var dmChannel = await user.CreateDMChannelAsync();

                if (hasAttachments)
                {
                    foreach (var attachment in attachments)
                    {
                        using var httpClient = new HttpClient();
                        var stream = await httpClient.GetStreamAsync(attachment.Url);
                        var file = new FileAttachment(stream, attachment.Filename);
                        await dmChannel.SendFileAsync(file, embed: embed.Build());
                    }
                }
                else
                {
                    await dmChannel.SendMessageAsync(embed: embed.Build());
                }

                var confirmationMessage = await ReplyAsync($"Message successfully sent to {user.Username}.");
                await Context.Message.DeleteAsync();
                await Task.Delay(TimeSpan.FromSeconds(10));
                await confirmationMessage.DeleteAsync();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Failed to send message to {user.Username}. Error: {ex.Message}");
            }
        }
    }
}
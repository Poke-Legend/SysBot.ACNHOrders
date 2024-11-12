using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.IO;
using System.Threading;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class SudoModule : ModuleBase<SocketCommandContext>
    {
        private const int GuildsPerPage = 25;
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        // Command to list all guilds the bot is part of
        [Command("listguilds")]
        [Alias("lg", "servers", "listservers")]
        [Summary("Lists all guilds the bot is part of.")]
        [RequireSudo]
        public async Task ListGuilds(int page = 1)
        {
            var guildCount = Context.Client.Guilds.Count;
            var totalPages = (int)Math.Ceiling(guildCount / (double)GuildsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var guilds = Context.Client.Guilds
                .Skip((page - 1) * GuildsPerPage)
                .Take(GuildsPerPage);

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"📜 List of Guilds - Page {page}/{totalPages}")
                .WithDescription("Here are the guilds I'm currently in:")
                .WithColor(EmbedColor);

            foreach (var guild in guilds)
            {
                embedBuilder.AddField(guild.Name, $"ID: {guild.Id}", inline: true);
            }

            var dmChannel = await Context.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embedBuilder.Build());

            var confirmationEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithDescription($"{Context.User.Mention}, I've sent you a DM with the list of guilds (Page {page}).");

            await ReplyAndDeleteAsync(embed: confirmationEmbed.Build());
        }

        [Command("addchannel")]
        [Summary("Adds the current channel to the list of channels for status updates.")]
        [RequireSudo]
        public async Task AddChannelAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            var embedBuilder = new EmbedBuilder().WithColor(EmbedColor);

            if (availableChannels.Contains(channelId))
            {
                embedBuilder.WithDescription("⚠️ This channel is already in the list.");
                await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
                return;
            }

            bool success = await ChannelManager.AddChannelAsync(channelId);
            embedBuilder.WithDescription(success
                ? $"✅ Channel **{Context.Channel.Name}** has been added to the list."
                : "❌ Failed to save the channel list. Please try again later.");

            await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        [Command("removechannel")]
        [Summary("Removes the current channel from the list of channels for status updates.")]
        [RequireSudo]
        public async Task RemoveChannelAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            var embedBuilder = new EmbedBuilder().WithColor(EmbedColor);

            if (!availableChannels.Contains(channelId))
            {
                embedBuilder.WithDescription("⚠️ This channel is not in the list.");
                await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
                return;
            }

            bool success = await ChannelManager.RemoveChannelAsync(channelId);
            embedBuilder.WithDescription(success
                ? $"✅ Channel **{Context.Channel.Name}** has been removed from the list."
                : "❌ Failed to save the channel list. Please try again later.");

            await ReplyAsync(embed: embedBuilder.Build()).ConfigureAwait(false);
        }

        [Command("dm")]
        [Summary("Sends a direct message to a specified user.")]
        [RequireSudo]
        public async Task DMUserAsync(SocketUser user, [Remainder] string message)
        {
            var embed = new EmbedBuilder()
                .WithTitle("📩 Private Message from the Bot Creator!")
                .WithDescription(message)
                .WithColor(EmbedColor)
                .WithThumbnailUrl("https://raw.githubusercontent.com/Poke-Legend/ACNH-Images/main/Images/Mail/MailBox.png")
                .WithTimestamp(DateTimeOffset.Now);

            var result = await SendMessageAsync(user, embed, Context.Message.Attachments);

            var confirmationEmbed = new EmbedBuilder()
                .WithDescription(result)
                .WithColor(EmbedColor);

            await ReplyAndDeleteAsync(embed: confirmationEmbed.Build());
        }

        // (Other methods like BanAsync, UnBanAsync, CheckBanAsync, BanServerAsync, UnbanServerAsync, etc., can be similarly formatted.)

        private static async Task<string> SendMessageAsync(SocketUser user, EmbedBuilder embed, IReadOnlyCollection<Attachment> attachments)
        {
            try
            {
                var dmChannel = await user.CreateDMChannelAsync();

                if (attachments.Any())
                {
                    using var httpClient = new HttpClient();
                    foreach (var attachment in attachments)
                    {
                        var stream = await httpClient.GetStreamAsync(attachment.Url);
                        var file = new FileAttachment(stream, attachment.Filename);
                        await dmChannel.SendFileAsync(file, embed: embed.Build());
                    }
                }
                else
                {
                    await dmChannel.SendMessageAsync(embed: embed.Build());
                }

                return $"✅ Message successfully sent to {user.Username}.";
            }
            catch (Exception ex)
            {
                return $"❌ Failed to send message to {user.Username}. Error: {ex.Message}";
            }
        }

        private async Task ReplyAndDeleteAsync(Embed embed)
        {
            var userMessage = await ReplyAsync(embed: embed);
            await Task.Delay(2000);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        }
    }
}

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using System.IO;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class SudoModule : ModuleBase<SocketCommandContext>
    {
        private const int GuildsPerPage = 25;
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        [Command("listguilds")]
        [Alias("lg", "servers", "listservers")]
        [Summary("Lists all guilds the bot is part of.")]
        [RequireSudo]
        public async Task ListGuilds(int page = 1)
        {
            var guildCount = Context.Client.Guilds.Count;
            var totalPages = (int)Math.Ceiling(guildCount / (double)GuildsPerPage);
            page = Math.Clamp(page, 1, totalPages);

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
            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            var embedBuilder = new EmbedBuilder().WithColor(EmbedColor);

            if (availableChannels.Contains(channelId))
            {
                embedBuilder.WithDescription("⚠️ This channel is already in the list.");
                await ReplyAsync(embed: embedBuilder.Build());
                return;
            }

            bool success = await ChannelManager.AddChannelAsync(channelId);
            embedBuilder.WithDescription(success
                ? $"✅ Channel **{Context.Channel.Name}** has been added to the list."
                : "❌ Failed to save the channel list. Please try again later.");

            await ReplyAsync(embed: embedBuilder.Build());
        }

        [Command("removechannel")]
        [Summary("Removes the current channel from the list of channels for status updates.")]
        [RequireSudo]
        public async Task RemoveChannelAsync()
        {
            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            var embedBuilder = new EmbedBuilder().WithColor(EmbedColor);

            if (!availableChannels.Contains(channelId))
            {
                embedBuilder.WithDescription("⚠️ This channel is not in the list.");
                await ReplyAsync(embed: embedBuilder.Build());
                return;
            }

            bool success = await ChannelManager.RemoveChannelAsync(channelId);
            embedBuilder.WithDescription(success
                ? $"✅ Channel **{Context.Channel.Name}** has been removed from the list."
                : "❌ Failed to save the channel list. Please try again later.");

            await ReplyAsync(embed: embedBuilder.Build());
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
            await userMessage.DeleteAsync();
        }
    }

    public static class ServerBanManager
    {
        private static readonly HashSet<string> BannedServerIds = new();

        public static bool IsServerBanned(string serverId) => BannedServerIds.Contains(serverId);

        public static void BanServer(string serverId) => BannedServerIds.Add(serverId);

        public static void UnbanServer(string serverId) => BannedServerIds.Remove(serverId);
    }

    public class ServerBan : ModuleBase<SocketCommandContext>
    {
        protected override void BeforeExecute(CommandInfo command)
        {
            if (ServerBanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                throw new InvalidOperationException("This server has been banned from using the bot.");
            }

            base.BeforeExecute(command);
        }

        [Command("bls")]
        [Summary("Bans a server by its server ID.")]
        [RequireSudo]
        public async Task BanServerAsync(string serverId)
        {
            if (BanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is already banned.");
                return;
            }

            await BanManager.BanServerAsync(serverId);
            await ReplyAsync($"Server {serverId} has been banned.");

            if (ulong.TryParse(serverId, out var guildId))
            {
                var guild = Context.Client.GetGuild(guildId);
                if (guild != null)
                {
                    var botMember = guild.CurrentUser;
                    if (botMember != null)
                    {
                        await guild.LeaveAsync();
                    }
                }
            }
        }

        [Command("ubls")]
        [Summary("Unbans a server by its server ID.")]
        [RequireSudo]
        public async Task UnbanServerAsync(string serverId)
        {
            if (!BanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is not in the ban list.");
                return;
            }

            await BanManager.UnbanServerAsync(serverId);
            await ReplyAsync($"Server {serverId} has been unbanned.");
        }

        [Command("checkbls")]
        [Summary("Checks a server's ban state by its server ID.")]
        [RequireSudo]
        public async Task CheckServerBanAsync(string serverId)
        {
            var isBanned = BanManager.IsServerBanned(serverId);
            var message = isBanned
                ? $"Server {serverId} is banned."
                : $"Server {serverId} is not banned.";
            await ReplyAsync(message);
        }
    }
}

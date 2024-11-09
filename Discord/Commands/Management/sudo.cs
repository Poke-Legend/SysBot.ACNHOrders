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
                .WithTitle($"List of Guilds - Page {page}/{totalPages}")
                .WithDescription("Here are the guilds I'm currently in:")
                .WithColor(EmbedColor);

            foreach (var guild in guilds)
            {
                embedBuilder.AddField(guild.Name, $"ID: {guild.Id}", inline: true);
            }

            var dmChannel = await Context.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embedBuilder.Build());

            await ReplyAndDeleteAsync($"{Context.User.Mention}, I've sent you a DM with the list of guilds (Page {page}).");
        }

        // Command to send a DM to a specific user
        [Command("dm")]
        [Summary("Sends a direct message to a specified user.")]
        [RequireSudo]
        public async Task DMUserAsync(SocketUser user, [Remainder] string message)
        {
            var embed = new EmbedBuilder
            {
                Title = "Private Message from the Bot Owner!",
                Description = message,
                Color = EmbedColor,
                Timestamp = DateTimeOffset.Now,
                ThumbnailUrl = "https://raw.githubusercontent.com/Poke-Legend/ACNH-Images/main/Images/Mail/MailBox.png"
            };

            var result = await SendMessageAsync(user, embed, Context.Message.Attachments);
            await ReplyAndDeleteAsync(result);
        }

        [Command("ban")]
        [Summary("Bans a user by their long number ID.")]
        [RequireSudo]
        public async Task BanAsync(string id)
        {
            try
            {
                if (BanManager.IsUserBanned(id))
                {
                    await ReplyAsync($"{id} is already banned.");
                }
                else
                {
                    _ = BanManager.BanUserAsync(id); // Fire-and-forget without awaiting, suppressing the warning
                    await ReplyAsync($"{id} has been banned.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in BanAsync: {ex.Message}");
                await ReplyAsync("An error occurred while trying to ban the user.");
            }
        }


        [Command("unban")]
        [Summary("Unbans a user by their long number ID.")]
        [RequireSudo]
        public async Task UnBanAsync(string id)
        {
            try
            {
                if (BanManager.IsUserBanned(id))
                {
                    await BanManager.UnbanUserAsync(id);
                    await ReplyAsync($"{id} has been unbanned.");
                }
                else
                {
                    await ReplyAsync($"{id} could not be found in the ban list.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in UnBanAsync: {ex.Message}");
                await ReplyAsync("An error occurred while trying to unban the user.");
            }
        }


        [Command("checkBan")]
        [Summary("Checks a user's ban state by their long number ID.")]
        [RequireSudo]
        public async Task CheckBanAsync(string id)
        {
            try
            {
                var isBanned = BanManager.IsUserBanned(id);
                await ReplyAsync(isBanned ? $"{id} is banned." : $"{id} is not banned.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in CheckBanAsync: {ex.Message}");
                await ReplyAsync("An error occurred while checking the user's ban state.");
            }
        }


        protected override async Task BeforeExecuteAsync(CommandInfo command)
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await ReplyAsync("This server is banned from using the bot.").ConfigureAwait(false);
                throw new InvalidOperationException("Server is banned");
            }

            await base.BeforeExecuteAsync(command);
        }


        [Command("ubls")]
        [Summary("Unbans a server by its server ID.")]
        [RequireSudo]
        public async Task UnbanServerAsync(string serverId)
        {
            if (!BanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is not in the ban list.").ConfigureAwait(false);
                return;
            }

            _ = BanManager.UnbanServerAsync(serverId);
            await ReplyAsync($"Server {serverId} has been unbanned.").ConfigureAwait(false);
        }

        [Command("bls")]
        [Summary("Bans a server by its server ID.")]
        [RequireSudo]
        public async Task BanServerAsync(string serverId)
        {
            if (BanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is already banned.").ConfigureAwait(false);
                return;
            }

            _ = BanManager.BanServerAsync(serverId);
            await ReplyAsync($"Server {serverId} has been banned.").ConfigureAwait(false);

            if (ulong.TryParse(serverId, out var guildId))
            {
                var guild = Context.Client.GetGuild(guildId);
                if (guild != null)
                {
                    await guild.LeaveAsync().ConfigureAwait(false);
                }
            }
        }

        [Command("checkbls")]
        [Summary("Checks a server's ban state by its server ID.")]
        [RequireSudo]
        public async Task CheckServerBanAsync(string serverId)
        {
            var message = BanManager.IsServerBanned(serverId)
                ? $"Server {serverId} is banned."
                : $"Server {serverId} is not banned.";
            await ReplyAsync(message).ConfigureAwait(false);
        }

        [Command("loadLayer")]
        [Summary("Changes the current refresher layer to a new .nhl field item layer")]
        [RequireSudo]
        public async Task SetFieldLayerAsync(string filename)
        {
            var bot = Globals.Bot;

            if (!bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"This command can only be used in dodo restore mode with refresh map set to true.").ConfigureAwait(false);
                return;
            }

            var bytes = bot.ExternalMap.GetNHL(filename);

            if (bytes == null)
            {
                await ReplyAsync($"File {filename} does not exist or does not have the correct .nhl extension.").ConfigureAwait(false);
                return;
            }

            var req = new MapOverrideRequest(Context.User.Username, bytes, filename);
            bot.MapOverrides.Enqueue(req);

            await ReplyAsync($"Map refresh layer set to: {Path.GetFileNameWithoutExtension(filename)}.").ConfigureAwait(false);
            Globals.Bot.CLayer = $"{Path.GetFileNameWithoutExtension(filename)}";

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

                return $"Message successfully sent to {user.Username}.";
            }
            catch (Exception ex)
            {
                return $"Failed to send message to {user.Username}. Error: {ex.Message}";
            }
        }

        private async Task ReplyAndDeleteAsync(string message)
        {
            var userMessage = await ReplyAsync(message);
            await Task.Delay(2000);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        }
    }
}

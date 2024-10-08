﻿using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    // Renamed static class for managing banned servers
    public static class ServerBanManager
    {
        private static readonly HashSet<string> BannedServerIds = new();

        public static bool IsServerBanned(string serverId) => BannedServerIds.Contains(serverId);

        public static void BanServer(string serverId) => BannedServerIds.Add(serverId);

        public static void UnbanServer(string serverId) => BannedServerIds.Remove(serverId);
    }

    // Non-static class for bot command handling
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

        [Command("ubls")]
        [Summary("Unbans a server by its server ID.")]
        [RequireOwner]
        public async Task UnbanServerAsync(string serverId)
        {
            if (!ServerBanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is not in the ban list.").ConfigureAwait(false);
                return;
            }

            ServerBanManager.UnbanServer(serverId);
            await ReplyAsync($"Server {serverId} has been unbanned.").ConfigureAwait(false);
        }

        [Command("bls")]
        [Summary("Bans a server by its server ID.")]
        [RequireOwner]
        public async Task BanServerAsync(string serverId)
        {
            if (ServerBanManager.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is already banned.").ConfigureAwait(false);
                return;
            }

            ServerBanManager.BanServer(serverId);
            await ReplyAsync($"Server {serverId} has been banned.").ConfigureAwait(false);

            if (ulong.TryParse(serverId, out var guildId))
            {
                var guild = Context.Client.GetGuild(guildId);

                // Check if guild is null
                if (guild != null)
                {
                    var botMember = guild.GetBotMember();

                    if (botMember != null)
                    {
                        await guild.LeaveAsync().ConfigureAwait(false);
                    }
                }
            }
        }

        [Command("checkbls")]
        [Summary("Checks a server's ban state by its server ID.")]
        [RequireOwner]
        public async Task CheckServerBanAsync(string serverId)
        {
            var message = ServerBanManager.IsServerBanned(serverId)
                ? $"Server {serverId} is banned."
                : $"Server {serverId} is not banned.";
            await ReplyAsync(message).ConfigureAwait(false);
        }
    }

    public static class GuildExtensions
    {
        public static SocketGuildUser? GetBotMember(this SocketGuild guild) => guild.CurrentUser;
    }
}

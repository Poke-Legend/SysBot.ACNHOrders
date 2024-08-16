using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public static class ServerBan
    {
        private static readonly HashSet<string> BannedServerIds = new();

        public static bool IsServerBanned(string serverId) => BannedServerIds.Contains(serverId);

        public static void BanServer(string serverId) => BannedServerIds.Add(serverId);

        public static void UnbanServer(string serverId) => BannedServerIds.Remove(serverId);
    }

    public class ServerBanModule : ModuleBase<SocketCommandContext>
    {
        protected override void BeforeExecute(CommandInfo command)
        {
            if (ServerBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                throw new Exception("This server has been banned from using the bot.");
            }
            base.BeforeExecute(command);
        }

        [Command("ubls")]
        [Summary("Unbans a server by its server ID.")]
        [RequireOwner]
        public async Task UnbanServerAsync(string serverId)
        {
            if (ServerBan.IsServerBanned(serverId))
            {
                ServerBan.UnbanServer(serverId);
                await ReplyAsync($"Server {serverId} has been unbanned.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"Server {serverId} is not in the ban list.").ConfigureAwait(false);
            }
        }

        [Command("bls")]
        [Summary("Bans a server by its server ID.")]
        [RequireOwner]
        public async Task BanServerAsync(string serverId)
        {
            if (ServerBan.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is already banned.").ConfigureAwait(false);
            }
            else
            {
                ServerBan.BanServer(serverId);
                await ReplyAsync($"Server {serverId} has been banned.").ConfigureAwait(false);

                if (ulong.TryParse(serverId, out var guildId))
                {
                    var guild = Context.Client.GetGuild(guildId);
                    if (guild?.GetBotMember() is not null)
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
            var message = ServerBan.IsServerBanned(serverId)
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

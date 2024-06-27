using Discord.Commands;
using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class ServerBan
    {
        private static HashSet<string> bannedServerIds = new HashSet<string>();

        // Checks if a server is banned.
        public static bool IsServerBanned(string serverId) => bannedServerIds.Contains(serverId);

        // Adds a server to the ban list.
        public static void BanServer(string serverId) => bannedServerIds.Add(serverId);

        // Removes a server from the ban list.
        public static void UnBanServer(string serverId) => bannedServerIds.Remove(serverId);
    }

    // Module for ban commands.
    public class ServerBanModule : ModuleBase<SocketCommandContext>
    {
        // Ensure the server is not banned before processing any commands.
        protected override void BeforeExecute(CommandInfo command)
        {
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                throw new Exception("This server has been banned from using the bot.");
            }
            base.BeforeExecute(command);
        }

        [Command("unbls")]
        [Summary("Unbans a server by its server ID.")]
        [RequireOwner] 
        public async Task UnBanServerAsync(string serverId)
        {
            if (GlobalBan.IsServerBanned(serverId))
            {
                GlobalBan.UnbanServer(serverId);
                await ReplyAsync($"Server {serverId} has been unbanned.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"Server {serverId} could not be found in the ban list.").ConfigureAwait(false);
            }
        }

        [Command("bls")]
        [Summary("Bans a server by its server ID.")]
        [RequireOwner]
        public async Task BanServerAsync(string serverId)
        {
            if (GlobalBan.IsServerBanned(serverId))
            {
                await ReplyAsync($"Server {serverId} is already banned.").ConfigureAwait(false);
            }
            else
            {
                GlobalBan.BanServer(serverId);
                await ReplyAsync($"Server {serverId} has been banned.").ConfigureAwait(false);

                // Check if the bot is in the server and kick it if necessary
                var guild = Context.Client.GetGuild(ulong.Parse(serverId));
                if (guild != null && guild.GetBotMember() != null)
                {
                    await guild.LeaveAsync().ConfigureAwait(false);
                }
            }
        }

        [Command("checkbls")]
        [Summary("Checks a server's ban state by its server ID.")]
        [RequireOwner]
        public async Task CheckServerBanAsync(string serverId) =>
            await ReplyAsync(GlobalBan.IsServerBanned(serverId) ? $"Server {serverId} is banned" : $"Server {serverId} is not banned").ConfigureAwait(false);
    }

    // Extension method to retrieve the bot member from a guild ID
    public static class GuildExtensions
    {
        public static SocketGuildUser? GetBotMember(this SocketGuild guild)
        {
            return guild.GetUser(guild.CurrentUser.Id);

        }
    }
}

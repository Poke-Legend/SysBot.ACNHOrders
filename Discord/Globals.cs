using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders
{
    /// <summary>
    /// A static class holding global references (bot, syscord, queue, permission manager).
    /// </summary>
    public static class Globals
    {
        public static SysCord Self { get; set; } = default!;
        public static CrossBot Bot { get; set; } = default!;
        public static QueueHub Hub { get; set; } = default!;
        public static PermissionManager Manager { get; set; } = new PermissionManager();
    }

    /// <summary>
    /// Basic config for permission checks (ignore all perms, allow commands, etc.).
    /// </summary>
    public class PermissionConfig
    {
        public bool IgnoreAllPermissions { get; set; } = false;
        public bool AcceptingCommands { get; set; } = true;

        /// <summary>
        /// Checks if userRoles contains the requiredRole.
        /// </summary>
        public bool GetHasRole(string requiredRole, IEnumerable<string> userRoles)
        {
            return userRoles.Contains(requiredRole);
        }
    }

    /// <summary>
    /// Manages the global sudo list and a <see cref="PermissionConfig"/>.
    /// </summary>
    public class PermissionManager
    {
        // Stores user IDs that can sudo
        private HashSet<ulong> GlobalSudoList { get; set; } = new();

        public PermissionConfig Config { get; set; } = new PermissionConfig();

        public PermissionManager()
        {
            // Initialize the sudo list from GitHub on creation
            LoadSudoList().Wait();
        }

        /// <summary>
        /// Returns true if userId is in the global sudo list.
        /// </summary>
        public bool CanUseSudo(ulong userId) => GlobalSudoList.Contains(userId);

        public async Task AddSudoAsync(ulong userId)
        {
            if (!GlobalSudoList.Contains(userId))
            {
                GlobalSudoList.Add(userId);
                await SaveSudoListAsync();
            }
        }

        public async Task RemoveSudoAsync(ulong userId)
        {
            if (GlobalSudoList.Contains(userId))
            {
                GlobalSudoList.Remove(userId);
                await SaveSudoListAsync();
            }
        }

        public IEnumerable<ulong> GetAllSudoUsers() => GlobalSudoList;

        private async Task LoadSudoList()
        {
            string? json = await GitHubApi.FetchFileContentAsync(GitHubApi.SudoApiUrl);
            if (!string.IsNullOrEmpty(json))
            {
                GlobalSudoList = JsonSerializer.Deserialize<HashSet<ulong>>(json) ?? new HashSet<ulong>();
            }
        }

        private async Task SaveSudoListAsync()
        {
            string content = JsonSerializer.Serialize(GlobalSudoList);
            string? sha = await GitHubApi.GetFileShaAsync(GitHubApi.SudoApiUrl);
            await GitHubApi.UpdateFileAsync(content, "Update sudo list", GitHubApi.SudoApiUrl, sha);
        }
    }

    /// <summary>
    /// A precondition that ensures only "sudo" users or the bot owner can run the command.
    /// </summary>
    public sealed class RequireSudoAttribute : PreconditionAttribute
    {
        public override async Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context,
            CommandInfo command,
            IServiceProvider services
        )
        {
            var manager = Globals.Manager;
            var config = manager.Config;

            // If ignoring perms, or user is sudo, or user is bot owner
            if (config.IgnoreAllPermissions || manager.CanUseSudo(context.User.Id) || context.User.Id == Globals.Self.Owner)
            {
                await TryDeleteCommandMessageAsync(context);
                return PreconditionResult.FromSuccess();
            }

            // If user is not a guild user, we can't check roles
            if (context.User is not SocketGuildUser guildUser)
                return PreconditionResult.FromError("You must be in a guild to run this command.");

            // If user is in the sudo list
            if (manager.CanUseSudo(guildUser.Id))
            {
                await TryDeleteCommandMessageAsync(context);
                return PreconditionResult.FromSuccess();
            }

            // Otherwise, no permission
            return PreconditionResult.FromError("You are not permitted to run this command.");
        }

        private static async Task TryDeleteCommandMessageAsync(ICommandContext context)
        {
            if (context.Guild != null && context.Channel is ITextChannel channel)
            {
                try
                {
                    var botUser = await context.Guild.GetCurrentUserAsync();
                    if (botUser.GetPermissions(channel).ManageMessages)
                    {
                        await context.Message.DeleteAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete command message: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// A precondition that requires the user to have a specific "queue role" to use the command,
    /// or be a sudo user, or bot owner.
    /// </summary>
    public sealed class RequireQueueRoleAttribute : PreconditionAttribute
    {
        private readonly string _requiredRole;

        public RequireQueueRoleAttribute(string requiredRole) => _requiredRole = requiredRole;

        public override async Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context,
            CommandInfo command,
            IServiceProvider services
        )
        {
            var manager = Globals.Manager;
            var config = manager.Config;

            // If user is sudo, is owner, or ignoring all perms
            if (manager.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || config.IgnoreAllPermissions)
            {
                await TryDeleteCommandMessageAsync(context);
                return PreconditionResult.FromSuccess();
            }

            // Must be a guild user to check roles
            if (context.User is not SocketGuildUser guildUser)
                return PreconditionResult.FromError("You must be in a guild to run this command.");

            // Are commands accepted at the moment?
            if (!config.AcceptingCommands)
                return PreconditionResult.FromError("Sorry, I am not currently accepting commands!");

            // Check if user has the required role by name
            bool hasRequiredRole = config.GetHasRole(_requiredRole, guildUser.Roles.Select(r => r.Name));
            if (!hasRequiredRole)
            {
                return PreconditionResult.FromError($"You do not have the '{_requiredRole}' role to run this command.");
            }

            // If we get here, the user has the required role
            await TryDeleteCommandMessageAsync(context);
            return PreconditionResult.FromSuccess();
        }

        private static async Task TryDeleteCommandMessageAsync(ICommandContext context)
        {
            if (context.Guild != null && context.Channel is ITextChannel channel)
            {
                try
                {
                    var botUser = await context.Guild.GetCurrentUserAsync();
                    if (botUser.GetPermissions(channel).ManageMessages)
                    {
                        await context.Message.DeleteAsync();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to delete command message: {ex.Message}");
                }
            }
        }
    }
}

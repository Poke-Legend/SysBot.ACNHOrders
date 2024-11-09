using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders
{
    public static class Globals
    {
        public static SysCord Self { get; set; } = default!;
        public static CrossBot Bot { get; set; } = default!;
        public static QueueHub Hub { get; set; } = default!;
        public static PermissionManager Manager { get; set; } = new PermissionManager();
    }

    public class PermissionConfig
    {
        public bool IgnoreAllPermissions { get; set; } = false;
        public bool AcceptingCommands { get; set; } = true;

        // Checks if the user has the required role
        public bool GetHasRole(string requiredRole, IEnumerable<string> userRoles)
        {
            return userRoles.Contains(requiredRole);
        }
    }

    public class PermissionManager
    {
        private HashSet<ulong> GlobalSudoList { get; set; } = new HashSet<ulong>();

        public PermissionConfig Config { get; set; } = new PermissionConfig();

        public PermissionManager()
        {
            // Initialize the sudo list
            LoadSudoList().Wait();
        }

        public bool CanUseSudo(ulong userId) => GlobalSudoList.Contains(userId);

        public async Task AddSudoAsync(ulong userId)
        {
            if (!GlobalSudoList.Contains(userId))
            {
                GlobalSudoList.Add(userId);
                await SaveSudoListAsync(); // Save the updated list to GitHub
            }
        }

        public async Task RemoveSudoAsync(ulong userId)
        {
            if (GlobalSudoList.Contains(userId))
            {
                GlobalSudoList.Remove(userId);
                await SaveSudoListAsync(); // Save the updated list to GitHub
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

        // Saves the sudo list to GitHub
        private async Task SaveSudoListAsync()
        {
            string content = JsonSerializer.Serialize(GlobalSudoList);
            string? sha = await GitHubApi.GetFileShaAsync(GitHubApi.SudoApiUrl);
            await GitHubApi.UpdateFileAsync(content, "Update sudo list", GitHubApi.SudoApiUrl, sha);
        }
    }


    public sealed class RequireSudoAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var manager = Globals.Manager;
            var config = manager.Config;

            // Check if permissions are ignored, or if the user is a sudo user or the bot owner
            if (config.IgnoreAllPermissions || manager.CanUseSudo(context.User.Id) || context.User.Id == Globals.Self.Owner)
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            // Ensure the user is a guild member (SocketGuildUser)
            if (context.User is not SocketGuildUser guildUser)
            {
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));
            }

            return manager.CanUseSudo(guildUser.Id)
                ? Task.FromResult(PreconditionResult.FromSuccess())
                : Task.FromResult(PreconditionResult.FromError("You are not permitted to run this command."));
        }
    }

    public sealed class RequireQueueRoleAttribute : PreconditionAttribute
    {
        private readonly string _requiredRole;

        public RequireQueueRoleAttribute(string requiredRole) => _requiredRole = requiredRole;

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var manager = Globals.Manager;
            var config = manager.Config;

            // Check for sudo access or owner privileges
            if (manager.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || config.IgnoreAllPermissions)
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Ensure the user is a guild member
            if (context.User is not SocketGuildUser guildUser)
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));

            // Check if commands are currently accepted
            if (!config.AcceptingCommands)
                return Task.FromResult(PreconditionResult.FromError("Sorry, I am not currently accepting commands!"));

            // Check if the user has the required role
            if (!config.GetHasRole(_requiredRole, guildUser.Roles.Select(role => role.Name)))
            {
                return Task.FromResult(PreconditionResult.FromError($"You do not have the {_requiredRole} role to run this command."));
            }

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}

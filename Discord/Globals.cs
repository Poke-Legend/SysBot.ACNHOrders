using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public static class Globals
    {
        public static SysCord Self { get; set; } = default!;
        public static CrossBot Bot { get; set; } = default!;
        public static QueueHub Hub { get; set; } = default!;
        public static PermissionManager Manager { get; set; } = new PermissionManager(); // Initializes and loads sudo.json
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
        private const string SudoFilePath = "sudo.json";
        private HashSet<ulong> GlobalSudoList { get; set; } = new HashSet<ulong>();

        public PermissionConfig Config { get; set; } = new PermissionConfig();

        public PermissionManager()
        {
            // Initialize the sudo list
            LoadSudoList();
        }

        public bool CanUseSudo(ulong userId) => GlobalSudoList.Contains(userId);

        public void AddSudo(ulong userId)
        {
            GlobalSudoList.Add(userId);
            SaveSudoList();
        }

        public void RemoveSudo(ulong userId)
        {
            GlobalSudoList.Remove(userId);
            SaveSudoList();
        }

        public IEnumerable<ulong> GetAllSudoUsers() => GlobalSudoList;

        // Loads the sudo list from sudo.json
        private void LoadSudoList()
        {
            if (File.Exists(SudoFilePath))
            {
                try
                {
                    var json = File.ReadAllText(SudoFilePath);
                    GlobalSudoList = JsonSerializer.Deserialize<HashSet<ulong>>(json) ?? new HashSet<ulong>();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading sudo list: {ex.Message}");
                    GlobalSudoList = new HashSet<ulong>();
                }
            }
        }

        // Saves the sudo list to sudo.json
        private void SaveSudoList()
        {
            try
            {
                var json = JsonSerializer.Serialize(GlobalSudoList);
                File.WriteAllText(SudoFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving sudo list: {ex.Message}");
            }
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
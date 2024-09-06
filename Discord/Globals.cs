using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json; // For JSON serialization
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

    // PermissionConfig class with permission settings
    public class PermissionConfig
    {
        public bool IgnoreAllPermissions { get; set; } = false;
        public bool AcceptingCommands { get; set; } = true;

        // Method to check if a user has a specific role
        public bool GetHasRole(string requiredRole, IEnumerable<string> userRoles)
        {
            return userRoles.Contains(requiredRole);
        }
    }

    // PermissionManager class with sudo logic and role checking
    public class PermissionManager
    {
        private const string SudoFilePath = "sudo.json";
        private HashSet<ulong> GlobalSudoList { get; set; }

        // Add the Config property
        public PermissionConfig Config { get; set; } = new PermissionConfig();

        public PermissionManager()
        {
            // Load the sudo list when the manager is initialized
            GlobalSudoList = LoadSudoList();
        }

        // Method to check if a user can use sudo based on their ID
        public bool CanUseSudo(ulong userId)
        {
            return GlobalSudoList.Contains(userId);
        }

        // Method to add a user to the sudo list and save to file
        public void AddSudo(ulong userId)
        {
            GlobalSudoList.Add(userId);
            SaveSudoList();
        }

        // Method to remove a user from the sudo list and save to file
        public void RemoveSudo(ulong userId)
        {
            GlobalSudoList.Remove(userId);
            SaveSudoList();
        }

        // Method to get all sudo users (optional)
        public IEnumerable<ulong> GetAllSudoUsers()
        {
            return GlobalSudoList;
        }

        // Load the sudo list from the sudo.json file
        private HashSet<ulong> LoadSudoList()
        {
            if (!File.Exists(SudoFilePath))
            {
                // If the file doesn't exist, create an empty list
                return new HashSet<ulong>();
            }

            try
            {
                // Read and deserialize the sudo list from the file
                var json = File.ReadAllText(SudoFilePath);
                var sudoList = JsonSerializer.Deserialize<HashSet<ulong>>(json);
                return sudoList ?? new HashSet<ulong>();
            }
            catch (Exception)
            {
                // If something goes wrong, return an empty list
                return new HashSet<ulong>();
            }
        }

        // Save the sudo list to the sudo.json file
        private void SaveSudoList()
        {
            var json = JsonSerializer.Serialize(GlobalSudoList);
            File.WriteAllText(SudoFilePath, json);
        }
    }

    // Precondition attribute for checking if the user has sudo permissions
    public sealed class RequireSudoAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var manager = Globals.Manager;  // Get the PermissionManager instance
            var config = manager.Config;    // Get the PermissionConfig instance

            // Check if permissions are ignored, the user has sudo, or the user is the bot owner
            if (config.IgnoreAllPermissions || manager.CanUseSudo(context.User.Id) || context.User.Id == Globals.Self.Owner)
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            // Ensure the user is in a guild context (SocketGuildUser)
            if (context.User is not SocketGuildUser guildUser)
            {
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));
            }

            // Check if the user ID is in the sudo list
            return manager.CanUseSudo(guildUser.Id)
                ? Task.FromResult(PreconditionResult.FromSuccess())
                : Task.FromResult(PreconditionResult.FromError("You are not permitted to run this command."));
        }
    }

    // Precondition attribute that checks for a specific role or permission level
    public sealed class RequireQueueRoleAttribute : PreconditionAttribute
    {
        private readonly string _name;

        // Constructor that allows specifying the required role
        public RequireQueueRoleAttribute(string name) => _name = name;

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var manager = Globals.Manager;
            var config = manager.Config;

            // Check if the user has sudo access or is the owner
            if (manager.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || config.IgnoreAllPermissions)
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Ensure the command is being run in a guild context (SocketGuildUser)
            if (context.User is not SocketGuildUser guildUser)
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));

            // If commands are not being accepted, reject the command
            if (!config.AcceptingCommands)
                return Task.FromResult(PreconditionResult.FromError("Sorry, I am not currently accepting commands!"));

            // Check if the user has the required role
            bool hasRole = config.GetHasRole(_name, guildUser.Roles.Select(z => z.Name));
            if (!hasRole)
                return Task.FromResult(PreconditionResult.FromError($"You do not have the {_name} role to run this command."));

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}

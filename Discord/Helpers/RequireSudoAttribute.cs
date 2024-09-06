using Discord.Commands;
using Discord.WebSocket;
using SysBot.ACNHOrders;
using System.Threading.Tasks;
using System;

public sealed class RequireSudoAttribute : PreconditionAttribute
{
    public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
    {
        var manager = Globals.Manager;  // Get the PermissionManager instance
        var config = manager.Config;    // Get the PermissionConfig instance from the manager

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

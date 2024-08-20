using System;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public sealed class RequireSudoAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var config = Globals.Bot.Config;

            if (config.IgnoreAllPermissions || config.CanUseSudo(context.User.Id) || context.User.Id == Globals.Self.Owner)
            {
                return Task.FromResult(PreconditionResult.FromSuccess());
            }

            if (context.User is not SocketGuildUser guildUser)
            {
                return Task.FromResult(PreconditionResult.FromError("You must be in a guild to run this command."));
            }

            return config.CanUseSudo(guildUser.Id)
                ? Task.FromResult(PreconditionResult.FromSuccess())
                : Task.FromResult(PreconditionResult.FromError("You are not permitted to run this command."));
        }
    }
}

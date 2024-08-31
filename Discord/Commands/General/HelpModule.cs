using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        private readonly CommandService _service;

        public HelpModule(CommandService service)
        {
            _service = service;
        }

        [Command("help")]
        [Summary("Lists available commands.")]
        [RequireSudo]
        public async Task HelpAsync()
        {
            if (IsServerBanned())
            {
                await LeaveServerAsync().ConfigureAwait(false);
                return;
            }

            var builder = CreateEmbedBuilder("These are the commands you can use:");
            var owner = (await GetApplicationOwnerAsync().ConfigureAwait(false)).Id;
            var userId = Context.User.Id;

            foreach (var module in _service.Modules)
            {
                var description = GetModuleCommandDescriptions(module, owner, userId);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    builder.AddField(module.Name, description, inline: false);
                }
            }

            await SendHelpToDMAsync(builder.Build());
        }

        [Command("help")]
        [Summary("Lists information about a specific command.")]
        public async Task HelpAsync([Summary("The command you want help for")] string command)
        {
            if (IsServerBanned())
            {
                await LeaveServerAsync().ConfigureAwait(false);
                return;
            }

            var result = _service.Search(Context, command);

            if (!result.IsSuccess)
            {
                await ReplyAsync($"Sorry, I couldn't find a command like **{command}**.").ConfigureAwait(false);
                return;
            }

            var builder = CreateEmbedBuilder($"Here are some commands like **{command}**:");

            foreach (var match in result.Commands)
            {
                var cmd = match.Command;
                builder.AddField(string.Join(", ", cmd.Aliases), GetCommandSummary(cmd), inline: false);
            }

            await SendHelpToDMAsync(builder.Build());
        }

        private async Task<IUser> GetApplicationOwnerAsync()
        {
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            return app.Owner;
        }

        private EmbedBuilder CreateEmbedBuilder(string description)
        {
            return new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = description
            };
        }

        private string GetModuleCommandDescriptions(ModuleInfo module, ulong owner, ulong userId)
        {
            var mentioned = new HashSet<string>();
            var descriptions = module.Commands
                .Where(cmd => !mentioned.Contains(cmd.Name))
                .Where(cmd => !RequiresOwner(cmd, owner, userId) && !RequiresSudo(cmd, userId))
                .Select(cmd => AddCommandIfValid(cmd, mentioned))
                .Where(description => !string.IsNullOrEmpty(description));

            return string.Join("\n", descriptions);
        }

        private static bool RequiresOwner(CommandInfo cmd, ulong owner, ulong userId)
        {
            return cmd.Attributes.Any(z => z is RequireOwnerAttribute) && owner != userId;
        }

        private static bool RequiresSudo(CommandInfo cmd, ulong userId)
        {
            return cmd.Attributes.Any(z => z is RequireSudoAttribute) && !Globals.Bot.Config.CanUseSudo(userId);
        }

        private string AddCommandIfValid(CommandInfo cmd, HashSet<string> mentioned)
        {
            var name = cmd.Name;
            mentioned.Add(name);

            var result = cmd.CheckPreconditionsAsync(Context).Result;
            return result.IsSuccess ? cmd.Aliases[0] : string.Empty;
        }

        private static string GetCommandSummary(CommandInfo cmd)
        {
            return $"Summary: {cmd.Summary}\nParameters: {GetParameterSummary(cmd.Parameters)}";
        }

        private static string GetParameterSummary(IReadOnlyList<ParameterInfo> parameters)
        {
            if (parameters.Count == 0)
                return "None";

            return $"{parameters.Count}\n- " + string.Join("\n- ", parameters.Select(GetParameterSummary));
        }

        private static string GetParameterSummary(ParameterInfo parameter)
        {
            var result = parameter.Name;
            if (!string.IsNullOrWhiteSpace(parameter.Summary))
                result += $" ({parameter.Summary})";

            return result;
        }

        private bool IsServerBanned()
        {
            return GlobalBan.IsServerBanned(Context.Guild.Id.ToString());
        }

        private async Task LeaveServerAsync()
        {
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }

        private async Task SendHelpToDMAsync(Embed embed)
        {
            try
            {
                await Context.User.SendMessageAsync(embed: embed).ConfigureAwait(false);
                await ReplyAsync("Help information has been sent to your DM.").ConfigureAwait(false);
            }
            catch
            {
                await ReplyAsync("Failed to send a DM. Please make sure your DMs are open.").ConfigureAwait(false);
            }
        }
    }
}

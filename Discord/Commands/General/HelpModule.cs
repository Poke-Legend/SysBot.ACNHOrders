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
        [Summary("Lists available commands and sends them as neat embeds in DM.")]
        public async Task HelpAsync()
        {
            if (IsServerBanned())
            {
                await LeaveServerAsync().ConfigureAwait(false);
                return;
            }

            var owner = (await GetApplicationOwnerAsync().ConfigureAwait(false)).Id;
            var userId = Context.User.Id;

            var helpEmbeds = new List<Embed>();

            // Collect all commands and descriptions, format them into neat embeds.
            foreach (var module in _service.Modules)
            {
                var embed = GetCommandDescriptions(module, owner, userId);
                if (embed != null)
                {
                    helpEmbeds.Add(embed);
                }
            }

            // If no commands were found, notify the user.
            if (!helpEmbeds.Any())
            {
                await ReplyAsync("No commands found.").ConfigureAwait(false);
                return;
            }

            // Send the help information as embeds in DM
            await SendHelpEmbedsToDMAsync(helpEmbeds);
        }

        private async Task SendHelpEmbedsToDMAsync(List<Embed> embeds)
        {
            try
            {
                foreach (var embed in embeds)
                {
                    await Context.User.SendMessageAsync(embed: embed).ConfigureAwait(false);
                }

                await ReplyAsync("Help information has been sent to your DM.").ConfigureAwait(false);
            }
            catch
            {
                await ReplyAsync("Failed to send a DM. Please make sure your DMs are open.").ConfigureAwait(false);
            }
        }

        private async Task<IUser> GetApplicationOwnerAsync()
        {
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            return app.Owner;
        }

        private Embed? GetCommandDescriptions(ModuleInfo module, ulong owner, ulong userId)
        {
            var mentioned = new HashSet<string>();
            var descriptions = module.Commands
                .Where(cmd => !mentioned.Contains(cmd.Name))
                .Where(cmd => !RequiresOwner(cmd, owner, userId) && !RequiresSudo(cmd, userId))
                .Select(cmd => AddCommandDescriptionWithNames(cmd, mentioned))
                .Where(description => !string.IsNullOrEmpty(description.Item1) && !string.IsNullOrEmpty(description.Item2))
                .ToList();

            if (!descriptions.Any())
                return null;

            // Create a clean embed with commands and descriptions
            var embedBuilder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Title = $"{module.Name}", // Module name serves as a category title
                Description = $"Here are the available commands:"
            };

            // Add each command's name and description to the embed
            foreach (var (commandName, commandSummary) in descriptions)
            {
                embedBuilder.AddField(commandName, commandSummary, inline: false);
            }

            return embedBuilder.Build();
        }

        private static bool RequiresOwner(CommandInfo cmd, ulong owner, ulong userId)
        {
            return cmd.Attributes.Any(z => z is RequireOwnerAttribute) && owner != userId;
        }

        private static bool RequiresSudo(CommandInfo cmd, ulong userId)
        {
            return cmd.Attributes.Any(z => z is RequireSudoAttribute) && !Globals.Bot.Config.CanUseSudo(userId);
        }

        private (string, string) AddCommandDescriptionWithNames(CommandInfo cmd, HashSet<string> mentioned)
        {
            var name = cmd.Name;
            mentioned.Add(name);

            var result = cmd.CheckPreconditionsAsync(Context).Result;
            if (result.IsSuccess)
            {
                var commandName = string.Join(", ", cmd.Aliases); // Show aliases as part of the command names
                var commandSummary = cmd.Summary ?? "No description available.";
                return (commandName, commandSummary);
            }

            return (string.Empty, string.Empty);
        }

        private bool IsServerBanned()
        {
            return GlobalBan.IsServerBanned(Context.Guild.Id.ToString());
        }

        private async Task LeaveServerAsync()
        {
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }
    }
}

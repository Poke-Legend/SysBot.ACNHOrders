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

            var helpEmbeds = await GetHelpEmbedsAsync();
            if (helpEmbeds.Any())
            {
                await SendHelpEmbedsToDMAsync(helpEmbeds);
            }
            else
            {
                await ReplyAsync("No commands found.").ConfigureAwait(false);
            }
        }

        private async Task<List<Embed>> GetHelpEmbedsAsync()
        {
            var owner = (await GetApplicationOwnerAsync().ConfigureAwait(false)).Id;
            var userId = Context.User.Id;
            var embeds = new List<Embed>();

            foreach (var module in _service.Modules)
            {
                var embed = GetCommandDescriptions(module, owner, userId);
                if (embed != null) embeds.Add(embed);
            }

            return embeds;
        }

        private Embed? GetCommandDescriptions(ModuleInfo module, ulong owner, ulong userId)
        {
            var descriptions = module.Commands
                .Where(cmd => IsCommandVisible(cmd, owner, userId))
                .Select(cmd => (cmd.Aliases.FirstOrDefault(), cmd.Summary ?? "No description available."))
                .Where(tuple => !string.IsNullOrEmpty(tuple.Item1))
                .ToList();

            if (!descriptions.Any()) return null;

            var embedBuilder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Title = $"{module.Name}",
                Description = "Here are the available commands:"
            };

            foreach (var (commandName, commandSummary) in descriptions)
            {
                embedBuilder.AddField(commandName, commandSummary, inline: false);
            }

            return embedBuilder.Build();
        }

        private bool IsCommandVisible(CommandInfo cmd, ulong owner, ulong userId)
        {
            return cmd.CheckPreconditionsAsync(Context).Result.IsSuccess &&
                   !RequiresOwner(cmd, owner, userId) &&
                   !RequiresSudo(cmd, userId);
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

        private static bool RequiresOwner(CommandInfo cmd, ulong owner, ulong userId)
        {
            return cmd.Attributes.Any(attr => attr is RequireOwnerAttribute) && owner != userId;
        }

        private static bool RequiresSudo(CommandInfo cmd, ulong userId)
        {
            return cmd.Attributes.Any(attr => attr is RequireSudoAttribute) && !Globals.Bot.Config.CanUseSudo(userId);
        }

        private async Task<IUser> GetApplicationOwnerAsync()
        {
            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            return app.Owner;
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

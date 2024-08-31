using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;

namespace SysBot.ACNHOrders.Discord.Commands.Bots
{
    public class RecipeModule : ModuleBase<SocketCommandContext>
    {
        private const int MinSearchTermLength = 2;
        private const int MaxResultLength = 500;
        private readonly Color NoMatchColor = Color.Red;
        private readonly Color MatchColor = Color.Green;
        private readonly Color TruncatedMatchColor = Color.Blue;

        [Command("recipeLang")]
        [Alias("rl")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task SearchItemsByLanguageAsync(string language, [Remainder] string itemName)
        {
            if (IsCommandInvalid()) return;

            var itemDataSource = GameInfo.GetStrings(language).ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        [Command("recipe")]
        [Alias("ri", "searchDIY")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task SearchItemsAsync([Remainder] string itemName)
        {
            if (IsCommandInvalid()) return;

            var itemDataSource = GameInfo.Strings.ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        private bool IsCommandInvalid()
        {
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                _ = Context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }

            if (!Globals.Bot.Config.AllowLookup)
            {
                _ = ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.").ConfigureAwait(false);
                return true;
            }

            return false;
        }

        private async Task ProcessItemSearchAsync(string itemName, IReadOnlyList<ComboItem> itemDataSource)
        {
            if (itemName.Length <= MinSearchTermLength)
            {
                await SendEmbedMessageAsync($"Please enter a search term longer than {MinSearchTermLength} characters.", NoMatchColor).ConfigureAwait(false);
                return;
            }

            var matches = FindMatchingItems(itemName, itemDataSource);

            if (!matches.Any())
            {
                await SendEmbedMessageAsync("No matches found.", NoMatchColor).ConfigureAwait(false);
                return;
            }

            await SendMatchedItemsAsync(matches).ConfigureAwait(false);
        }

        private IEnumerable<string> FindMatchingItems(string itemName, IReadOnlyList<ComboItem> itemDataSource)
        {
            return itemDataSource
                .Where(item => string.Equals(item.Text, itemName, StringComparison.OrdinalIgnoreCase) || item.Text.Contains(itemName, StringComparison.OrdinalIgnoreCase))
                .Where(item => ItemParser.InvertedRecipeDictionary.TryGetValue((ushort)item.Value, out _))
                .Select(item => $"{item.Value:X4} {item.Text}: Recipe order code: {ItemParser.InvertedRecipeDictionary[(ushort)item.Value]:X3}000016A2");
        }

        private async Task SendMatchedItemsAsync(IEnumerable<string> matches)
        {
            var result = string.Join(Environment.NewLine, matches);
            if (result.Length > MaxResultLength)
                result = result.Substring(0, MaxResultLength) + "...[truncated]";

            await SendEmbedMessageAsync(result, TruncatedMatchColor).ConfigureAwait(false);
        }

        private async Task SendEmbedMessageAsync(string description, Color color)
        {
            var embed = new EmbedBuilder()
                .WithDescription(description)
                .WithColor(color)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Bots
{
    /// <summary>
    /// Command module for searching DIY recipe IDs by item name.
    /// </summary>
    public class RecipeModule : ModuleBase<SocketCommandContext>
    {
        private const int MinSearchTermLength = 2;
        private const int MaxResultLength = 500;

        private readonly Color NoMatchColor = Color.Red;
        private readonly Color MatchColor = Color.Green;
        private readonly Color TruncatedMatchColor = Color.Blue;

        [Command("recipeLang")]
        [Alias("rl")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested item name string, in a specific language.")]
        public async Task SearchItemsByLanguageAsync(string language, [Remainder] string itemName)
        {
            var itemDataSource = GameInfo.GetStrings(language).ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        [Command("recipe")]
        [Alias("ri", "searchDIY")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested item name string.")]
        public async Task SearchItemsAsync([Remainder] string itemName)
        {
            var itemDataSource = GameInfo.Strings.ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        /// <summary>
        /// Validates user input, finds matches, and sends them to the channel.
        /// </summary>
        private async Task ProcessItemSearchAsync(string itemName, IReadOnlyList<ComboItem> itemDataSource)
        {
            if (itemName.Length <= MinSearchTermLength)
            {
                var shortSearchEmbed = new EmbedBuilder()
                    .WithColor(NoMatchColor)
                    .WithTitle("⚠️ Search Term Too Short")
                    .WithDescription($"Please enter a search term longer than {MinSearchTermLength} characters.")
                    .WithFooter("Try a longer search term for better results.")
                    .Build();

                await ReplyAsync(embed: shortSearchEmbed).ConfigureAwait(false);
                return;
            }

            var matches = FindMatchingItems(itemName, itemDataSource);

            if (!matches.Any())
            {
                var noMatchEmbed = new EmbedBuilder()
                    .WithColor(NoMatchColor)
                    .WithTitle("❌ No Matches Found")
                    .WithDescription("No recipes found matching your search term.")
                    .WithFooter("Try refining your search.")
                    .Build();

                await ReplyAsync(embed: noMatchEmbed).ConfigureAwait(false);
                return;
            }

            await SendMatchedItemsAsync(matches).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns a collection of matching lines that show the item name, ID, and the "recipe order code."
        /// </summary>
        private IEnumerable<string> FindMatchingItems(string itemName, IReadOnlyList<ComboItem> itemDataSource)
        {
            return itemDataSource
                .Where(item =>
                    string.Equals(item.Text, itemName, StringComparison.OrdinalIgnoreCase)
                    || item.Text.Contains(itemName, StringComparison.OrdinalIgnoreCase)
                )
                .Where(item => ItemParser.InvertedRecipeDictionary.TryGetValue((ushort)item.Value, out _))
                .Select(item =>
                {
                    // e.g., "1A2B SomeName: Recipe order code: 123000016A2"
                    var recipeCode = ItemParser.InvertedRecipeDictionary[(ushort)item.Value];
                    return $"{item.Value:X4} {item.Text}: Recipe order code: {recipeCode:X3}000016A2";
                });
        }

        /// <summary>
        /// Sends the matched items to the channel, truncating if necessary.
        /// </summary>
        private async Task SendMatchedItemsAsync(IEnumerable<string> matches)
        {
            var result = string.Join(Environment.NewLine, matches);
            bool isTruncated = false;

            if (result.Length > MaxResultLength)
            {
                result = result.Substring(0, MaxResultLength) + "...[truncated]";
                isTruncated = true;
            }

            var color = isTruncated ? TruncatedMatchColor : MatchColor;
            var description = isTruncated
                ? $"{result}\n*Results truncated due to length.*"
                : result;

            var matchesEmbed = new EmbedBuilder()
                .WithColor(color)
                .WithTitle("🔍 Recipe Search Results")
                .WithDescription(description)
                .WithFooter(isTruncated ? "Only partial results are shown." : "Full results shown.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: matchesEmbed).ConfigureAwait(false);
        }

        /// <summary>
        /// Optionally used to send generic embedded messages if needed.
        /// </summary>
        private async Task SendEmbedMessageAsync(string description, Color color)
        {
            var embed = new EmbedBuilder()
                .WithDescription(description)
                .WithColor(color)
                .WithFooter("Thank you for using the Recipe Bot!")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }
    }
}

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
    public class Recipe : ModuleBase<SocketCommandContext>
    {
        private const int MinSearchTermLength = 2;
        private const int MaxResultLength = 500;
        private readonly Color NoMatchColor = Color.Red;
        private readonly Color MatchColor = Color.Green;
        private readonly Color TruncatedMatchColor = Color.Blue;

        [Command("recipeLang")]
        [Alias("rl")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
        public async Task SearchItemsByLanguageAsync(string language, [Remainder] string itemName)
        {
            if (await IsCommandInvalidAsync()) return;

            var itemDataSource = GameInfo.GetStrings(language).ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        [Command("recipe")]
        [Alias("ri", "searchDIY")]
        [Summary("Gets a list of DIY recipe IDs that contain the requested Item Name string.")]
        public async Task SearchItemsAsync([Remainder] string itemName)
        {
            if (await IsCommandInvalidAsync()) return;

            var itemDataSource = GameInfo.Strings.ItemDataSource;
            await ProcessItemSearchAsync(itemName, itemDataSource).ConfigureAwait(false);
        }

        private async Task<bool> IsCommandInvalidAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }

            if (!Globals.Bot.Config.AllowLookup)
            {
                var disabledEmbed = new EmbedBuilder()
                    .WithColor(NoMatchColor)
                    .WithTitle("🔒 Lookup Disabled")
                    .WithDescription($"{Context.User.Mention}, lookup commands are currently disabled.")
                    .WithFooter("Please check back later.")
                    .Build();
                await ReplyAsync(embed: disabledEmbed).ConfigureAwait(false);
                return true;
            }

            return false;
        }

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
            bool isTruncated = false;

            if (result.Length > MaxResultLength)
            {
                result = result.Substring(0, MaxResultLength) + "...[truncated]";
                isTruncated = true;
            }

            var color = isTruncated ? TruncatedMatchColor : MatchColor;
            var description = isTruncated ? $"{result}\n*Results truncated due to length.*" : result;

            var matchesEmbed = new EmbedBuilder()
                .WithColor(color)
                .WithTitle("🔍 Recipe Search Results")
                .WithDescription(description)
                .WithFooter(isTruncated ? "Only partial results are shown." : "Full results shown.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: matchesEmbed).ConfigureAwait(false);
        }

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

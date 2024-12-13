﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Bots
{
    public class Item : ModuleBase<SocketCommandContext>
    {

        [Command("lookupLang")]
        [Alias("ll")]
        [Summary("Gets a list of items that contain the request string.")]
        public async Task SearchItemsAsync([Summary("Language code to search with")] string language, [Summary("Item name / item substring")][Remainder] string itemName)
        {
            var strings = GameInfo.GetStrings(language).ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("lookup")]
        [Alias("li", "search")]
        [Summary("Gets a list of items that contain the request string.")]
        public async Task SearchItemsAsync([Summary("Item name / item substring")][Remainder] string itemName)
        {
            var strings = GameInfo.Strings.ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("item")]
        [Summary("Gets the info for an item.")]
        public async Task GetItemInfoAsync([Summary("Item ID (in hex)")] string itemHex)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("❌ Invalid Item")
                    .WithDescription("The item you requested could not be found. Please check the item ID.")
                    .WithColor(Color.Red)
                    .WithFooter("Request another item using a valid ID.");

                await ReplyAsync(embed: errorEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var name = GameInfo.Strings.GetItemName(itemID);
            var result = ItemInfo.GetItemInfo(itemID);
            if (result.Length == 0)
            {
                var noDataEmbed = new EmbedBuilder()
                    .WithTitle($"ℹ️ Item Info: {name}")
                    .WithDescription("No customization data available for this item.")
                    .WithColor(Color.LightGrey)
                    .WithFooter("Use a different item ID to view customization details.");

                await ReplyAsync(embed: noDataEmbed.Build()).ConfigureAwait(false);
            }
            else
            {
                var infoEmbed = new EmbedBuilder()
                    .WithTitle($"📜 Item Info: {name}")
                    .WithDescription(result)
                    .WithColor(Color.Blue)
                    .WithFooter("For detailed information, refer to the database.")
                    .WithTimestamp(DateTimeOffset.Now);

                await ReplyAsync(embed: infoEmbed.Build()).ConfigureAwait(false);
            }
        }

        [Command("stack")]
        [Summary("Stacks an item and prints the hex code.")]
        public async Task StackAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Count of items in the stack")] int count)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE || count < 1 || count > 99)
            {
                var invalidStackEmbed = new EmbedBuilder()
                    .WithTitle("❌ Invalid Stack")
                    .WithDescription("The item ID or count is invalid. Ensure the count is between 1 and 99.")
                    .WithColor(Color.Red)
                    .WithFooter("Enter valid details to stack items successfully.");

                await ReplyAsync(embed: invalidStackEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var ct = count - 1;
            var item = new NHSE.Core.Item(itemID) { Count = (ushort)ct };
            var msg = ItemParser.GetItemText(item);

            var stackEmbed = new EmbedBuilder()
                .WithTitle("📦 Stacked Item")
                .WithDescription(msg)
                .WithColor(Color.Purple)
                .AddField("Item Count", count.ToString(), true)
                .AddField("Hex Code", itemHex, true)
                .WithFooter("Item stacking successful!")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: stackEmbed.Build()).ConfigureAwait(false);
        }

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("First customization value")] int cust1, [Summary("Second customization value")] int cust2)
        {
            await CustomizeAsync(itemHex, cust1 + cust2).ConfigureAwait(false);
        }

        [Command("customize")]
        [Summary("Customizes an item and prints the hex code.")]
        public async Task CustomizeAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Customization value sum")] int sum)
        {
            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE)
            {
                var invalidItemEmbed = new EmbedBuilder()
                    .WithTitle("❌ Invalid Item")
                    .WithDescription("The item you requested could not be found. Please check the item ID.")
                    .WithColor(Color.Red)
                    .WithFooter("Request another item using a valid ID.");

                await ReplyAsync(embed: invalidItemEmbed.Build()).ConfigureAwait(false);
                return;
            }
            if (sum <= 0)
            {
                var noCustomizationEmbed = new EmbedBuilder()
                    .WithTitle("❗ No Customization")
                    .WithDescription("No customization data specified. Please enter a valid customization value.")
                    .WithColor(Color.Orange)
                    .WithFooter("Customization not applied.");

                await ReplyAsync(embed: noCustomizationEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
            {
                var noDataEmbed = new EmbedBuilder()
                    .WithTitle("❗ No Customization Data")
                    .WithDescription("No customization data available for the requested item.")
                    .WithColor(Color.LightGrey)
                    .WithFooter("Select a different item ID to apply customization.");

                await ReplyAsync(embed: noDataEmbed.Build()).ConfigureAwait(false);
                return;
            }

            int body = sum & 7;
            int fabric = sum >> 5;
            if (fabric > 7 || (fabric << 5 | body) != sum)
            {
                var invalidDataEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ Invalid Customization Data")
                    .WithDescription("The specified customization data is invalid.")
                    .WithColor(Color.Red)
                    .WithFooter("Customization not applied. Check the input values.");

                await ReplyAsync(embed: invalidDataEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var item = new NHSE.Core.Item(itemID) { BodyType = body, PatternChoice = fabric };
            var msg = ItemParser.GetItemText(item);

            var customizedEmbed = new EmbedBuilder()
                .WithTitle("🎨 Customized Item")
                .WithDescription(msg)
                .WithColor(Color.Orange)
                .AddField("Body Type", body.ToString(), true)
                .AddField("Pattern Choice", fabric.ToString(), true)
                .WithFooter("Customization applied successfully.")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: customizedEmbed.Build()).ConfigureAwait(false);
        }

        private async Task PrintItemsAsync(string itemName, IReadOnlyList<ComboItem> strings)
        {
            const int minLength = 2;
            if (itemName.Length <= minLength)
            {
                var tooShortEmbed = new EmbedBuilder()
                    .WithTitle("⚠️ Search Term Too Short")
                    .WithDescription($"Please enter a search term longer than {minLength} characters.")
                    .WithColor(Color.Red)
                    .WithFooter("Try again with a longer term.");

                await ReplyAsync(embed: tooShortEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var matches = ItemParser.GetItemsMatching(itemName, strings).ToArray();
            var result = string.Join(Environment.NewLine, matches.Select(z => $"{z.Value:X4} {z.Text}"));

            if (result.Length == 0)
            {
                var noResultsEmbed = new EmbedBuilder()
                    .WithTitle("🔍 No Matches Found")
                    .WithDescription("No items matched your search. Please refine your keywords.")
                    .WithColor(Color.Red)
                    .WithFooter("Use different keywords for better results.");

                await ReplyAsync(embed: noResultsEmbed.Build()).ConfigureAwait(false);
                return;
            }

            const int maxLength = 500;
            if (result.Length > maxLength)
            {
                result = result.Substring(0, maxLength) + "...[truncated]";
            }

            var codeEmbed = new EmbedBuilder()
                .WithTitle("🔍 Search Result")
                .WithDescription(Format.Code(result))
                .WithColor(Color.Green)
                .WithFooter("Use a more specific term if you got too many results.")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: codeEmbed.Build()).ConfigureAwait(false);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
            if (await CheckBanAndPermissionsAsync()) return;

            var strings = GameInfo.GetStrings(language).ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("lookup")]
        [Alias("li", "search")]
        [Summary("Gets a list of items that contain the request string.")]
        public async Task SearchItemsAsync([Summary("Item name / item substring")][Remainder] string itemName)
        {
            if (await CheckBanAndPermissionsAsync()) return;

            var strings = GameInfo.Strings.ItemDataSource;
            await PrintItemsAsync(itemName, strings).ConfigureAwait(false);
        }

        [Command("item")]
        [Summary("Gets the info for an item.")]
        public async Task GetItemInfoAsync([Summary("Item ID (in hex)")] string itemHex)
        {
            if (await CheckBanAndPermissionsAsync()) return;

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE)
            {
                await base.ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var name = GameInfo.Strings.GetItemName(itemID);
            var result = ItemInfo.GetItemInfo(itemID);
            if (result.Length == 0)
            {
                await ReplyAsync($"No customization data available for the requested item ({name}).").ConfigureAwait(false);
            }
            else
            {
                var responseEmbed = new EmbedBuilder()
                    .WithTitle($"Item Info: {name}")
                    .WithDescription(result)
                    .WithColor(Color.Blue)
                    .WithThumbnailUrl("https://example.com/item-image.png") // Add a relevant image URL
                    .WithFooter("For more details, check our database.")
                    .WithTimestamp(DateTimeOffset.Now);

                await ReplyAsync(embed: responseEmbed.Build()).ConfigureAwait(false);
            }
        }

        [Command("stack")]
        [Summary("Stacks an item and prints the hex code.")]
        public async Task StackAsync([Summary("Item ID (in hex)")] string itemHex, [Summary("Count of items in the stack")] int count)
        {
            if (await CheckBanAndPermissionsAsync()) return;

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE || count < 1 || count > 99)
            {
                await base.ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }

            var ct = count - 1; // value 0 => count of 1
            var item = new NHSE.Core.Item(itemID) { Count = (ushort)ct };
            var msg = ItemParser.GetItemText(item);

            var responseEmbed = new EmbedBuilder()
                .WithTitle("Stacked Item")
                .WithDescription(msg)
                .WithColor(Color.Purple)
                .AddField("Item Count", count.ToString(), true)
                .AddField("Hex Code", itemHex, true)
                .WithFooter("Item stacking successful!")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: responseEmbed.Build()).ConfigureAwait(false);
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
            if (await CheckBanAndPermissionsAsync()) return;

            ushort itemID = ItemParser.GetID(itemHex);
            if (itemID == NHSE.Core.Item.NONE)
            {
                await base.ReplyAsync("Invalid item requested.").ConfigureAwait(false);
                return;
            }
            if (sum <= 0)
            {
                await ReplyAsync("No customization data specified.").ConfigureAwait(false);
                return;
            }

            var remake = ItemRemakeUtil.GetRemakeIndex(itemID);
            if (remake < 0)
            {
                await ReplyAsync("No customization data available for the requested item.").ConfigureAwait(false);
                return;
            }

            int body = sum & 7;
            int fabric = sum >> 5;
            if (fabric > 7 || (fabric << 5 | body) != sum)
            {
                await ReplyAsync("Invalid customization data specified.").ConfigureAwait(false);
                return;
            }

            var info = ItemRemakeInfoData.List[remake];
            bool hasBody = body == 0 || body <= info.ReBodyPatternNum;
            bool hasFabric = fabric == 0 || info.GetFabricDescription(fabric) != "Invalid";

            if (!hasBody || !hasFabric)
            {
                await ReplyAsync("Requested customization for item appears to be invalid.").ConfigureAwait(false);
                return;
            }

            var item = new NHSE.Core.Item(itemID) { BodyType = body, PatternChoice = fabric };
            var msg = ItemParser.GetItemText(item);

            var responseEmbed = new EmbedBuilder()
                .WithTitle("Customized Item")
                .WithDescription(msg)
                .WithColor(Color.Orange)
                .AddField("Body Type", body.ToString(), true)
                .AddField("Pattern Choice", fabric.ToString(), true)
                .WithFooter("Customization applied successfully.")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: responseEmbed.Build()).ConfigureAwait(false);
        }

        private async Task<bool> CheckBanAndPermissionsAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }

            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Lookup commands are not accepted.");
                return true;
            }

            return false;
        }

        private async Task PrintItemsAsync(string itemName, IReadOnlyList<ComboItem> strings)
        {
            const int minLength = 2;
            if (itemName.Length <= minLength)
            {
                await ReplyAsync($"Please enter a search term longer than {minLength} characters.").ConfigureAwait(false);
                return;
            }

            var exact = ItemParser.GetItem(itemName, strings);
            if (!exact.IsNone)
            {
                var msg = $"{exact.ItemId:X4} {itemName}";
                if (msg == "02F8 vine")
                {
                    msg = "3107 vine";
                }
                if (msg == "02F7 glowing moss")
                {
                    msg = "3106 glowing moss";
                }
                await ReplyAsync(Format.Code(msg)).ConfigureAwait(false);
                return;
            }

            var matches = ItemParser.GetItemsMatching(itemName, strings).ToArray();
            var result = string.Join(Environment.NewLine, matches.Select(z => $"{z.Value:X4} {z.Text}"));

            if (result.Length == 0)
            {
                var responseEmbed = new EmbedBuilder()
                    .WithTitle("Search Result")
                    .WithDescription("No matches found for your search.")
                    .WithColor(Color.Red)
                    .WithFooter("Try using different keywords or check your spelling.")
                    .WithTimestamp(DateTimeOffset.Now);

                await ReplyAsync(embed: responseEmbed.Build()).ConfigureAwait(false);
                return;
            }

            const int maxLength = 500;
            if (result.Length > maxLength)
            {
                var ordered = matches.OrderBy(z => LevenshteinDistance.Compute(z.Text, itemName));
                result = string.Join(Environment.NewLine, ordered.Select(z => $"{z.Value:X4} {z.Text}"));
                result = result.Substring(0, maxLength) + "...[truncated]";
            }

            var codeEmbed = new EmbedBuilder()
                .WithTitle("Search Result")
                .WithDescription(Format.Code(result))
                .WithColor(Color.Green)
                .WithFooter("Use a more specific term if you got too many results.")
                .WithTimestamp(DateTimeOffset.Now);

            await ReplyAsync(embed: codeEmbed.Build()).ConfigureAwait(false);
        }
    }
}

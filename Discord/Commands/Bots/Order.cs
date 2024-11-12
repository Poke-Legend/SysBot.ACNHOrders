using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.ACNHOrders.Discord.Helpers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public class Order : ModuleBase<SocketCommandContext>
    {
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;
        private static readonly Dictionary<ulong, DateTime> UserLastCommand = new();
        private static readonly object CommandSync = new();

        private const string OrderItemSummary =
            "Requests the bot to add the item order to the queue based on user input. " +
            "Hex Mode: Provide item IDs in hex, separated by spaces. " +
            "Text Mode: Provide item names, separated by commas. " +
            "To parse for another language, include the language code first, followed by a comma and the items.";

        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        [Command("order")]
        [Summary(OrderItemSummary)]
        public async Task RequestOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            if (await IsUserBannedAsync() || await IsServerBannedAsync())
                return;

            var cfg = Globals.Bot.Config;
            LogUtil.LogInfo($"Order received by {Context.User.Username} - {request}", nameof(Order));

            // Attempt to parse villager order
            var result = VillagerOrderParser.ExtractVillagerName(request, out var villagerName, out var sanitizedOrder);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                var invalidEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Invalid Villager Requested")
                    .WithDescription($"{Context.User.Mention}, {villagerName} is not adoptable. Order not accepted.")
                    .Build();
                await ReplyAsync(embed: invalidEmbed).ConfigureAwait(false);
                return;
            }

            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    var disabledEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("❌ Villager Injection Disabled")
                        .WithDescription($"{Context.User.Mention}, villager injection is currently disabled.")
                        .Build();
                    await ReplyAsync(embed: disabledEmbed).ConfigureAwait(false);
                    return;
                }

                request = sanitizedOrder;
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(Context.User.Username, villager, 0, GameInfo.Strings.GetVillager(villagerName));
            }

            var itemsList = (await GetItemsFromRequestAsync(request, cfg, Context.Message.Attachments.FirstOrDefault())).ToList();
            if (itemsList == null || !itemsList.Any())
            {
                var noItemsEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ No Valid Items")
                    .WithDescription("No valid items or NHI attachment provided.")
                    .Build();
                await ReplyAsync(embed: noItemsEmbed).ConfigureAwait(false);
                return;
            }

            var items = AdjustItemListToTargetCount(itemsList, 40);
            await AttemptToQueueRequest(items.ToArray(), Context.User, Context.Channel, villagerRequest).ConfigureAwait(false);
        }

        private static List<Item> AdjustItemListToTargetCount(List<Item> itemsList, int targetCount)
        {
            var items = itemsList.ToList();
            var random = new Random();
            while (items.Count < targetCount)
            {
                items.Add(items[random.Next(itemsList.Count)]);
            }
            return items;
        }

        private static async Task<Item[]> GetItemsFromRequestAsync(string request, CrossBotConfig cfg, Attachment? attachment)
        {
            if (attachment != null)
            {
                var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
                if (att.Success && att.Data is Item[] itemData)
                {
                    return itemData;
                }
            }

            return string.IsNullOrWhiteSpace(request)
                ? new[] { new Item(Item.NONE) }
                : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();
        }

        private async Task AttemptToQueueRequest(
            IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel,
            VillagerRequest? villagerRequest, bool catalogue = false)
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode || Globals.Bot.Config.SkipConsoleBotCreation)
            {
                var unavailableEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Orders Not Accepted")
                    .WithDescription($"{Context.User.Mention}, orders are not currently accepted.")
                    .Build();
                await ReplyAsync(embed: unavailableEmbed).ConfigureAwait(false);
                return;
            }

            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            if (BanManager.IsUserBanned(orderer.Id.ToString()))
            {
                var bannedEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Banned")
                    .WithDescription($"{Context.User.Mention}, you have been banned for abuse. Order not accepted.")
                    .Build();
                await ReplyAsync(embed: bannedEmbed).ConfigureAwait(false);
                return;
            }

            if (Globals.Hub.Orders.Count >= MaxOrderCount)
            {
                var queueLimitEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Queue Limit Reached")
                    .WithDescription("The queue limit has been reached. Please try again later.")
                    .Build();
                await ReplyAsync(embed: queueLimitEmbed).ConfigureAwait(false);
                return;
            }

            if (!InternalItemTool.CurrentInstance.IsSane(items.ToArray(), Globals.Bot.Config.DropConfig))
            {
                var unsafeItemsEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Unsafe Items")
                    .WithDescription($"{Context.User.Mention}, you attempted to order items that could damage your save. Order not accepted.")
                    .Build();
                await ReplyAsync(embed: unsafeItemsEmbed).ConfigureAwait(false);
                return;
            }

            var multiOrder = new MultiItem(items.ToArray(), catalogue, true);
            var requestInfo = new OrderRequest<Item>(multiOrder, items.ToArray(), orderer.Id, QueueExtensions.GetNextID(), orderer, msgChannel, villagerRequest);

            var queuedEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("✅ Order Queued")
                .WithDescription($"Your order has been added to the queue, {Context.User.Mention}. It will be processed shortly.")
                .WithFooter("Thank you for your patience")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();
            await ReplyAsync(embed: queuedEmbed).ConfigureAwait(false);

            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }

        private async Task<bool> IsUserBannedAsync()
        {
            if (BanManager.IsUserBanned(Context.User.Id.ToString()))
            {
                var bannedEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Banned")
                    .WithDescription($"{Context.User.Mention}, you have been banned for abuse. Order not accepted.")
                    .Build();
                await ReplyAsync(embed: bannedEmbed).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<bool> IsServerBannedAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }
            return false;
        }

        public static class VillagerOrderParser
        {
            public enum VillagerRequestResult { NoVillagerRequested, InvalidVillagerRequested, Success }

            private static readonly List<string> UnadoptableVillagers = new()
            {
                "cbr18", "der10", "elp11", // Add more unadoptable villagers as needed
            };

            public static VillagerRequestResult ExtractVillagerName(string order, out string result, out string sanitizedOrder, string villagerFormat = "Villager:")
            {
                result = string.Empty;
                sanitizedOrder = string.Empty;

                var index = order.IndexOf(villagerFormat, StringComparison.InvariantCultureIgnoreCase);
                if (index < 0 || index + villagerFormat.Length >= order.Length) return VillagerRequestResult.NoVillagerRequested;

                var internalName = order.Substring(index + villagerFormat.Length).Trim();
                var nameSearched = internalName;

                if (!VillagerResources.IsVillagerDataKnown(internalName))
                {
                    internalName = GameInfo.Strings.VillagerMap.FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase)).Key;
                }

                if (IsUnadoptable(nameSearched) || IsUnadoptable(internalName))
                {
                    result = $"{nameSearched} is not adoptable. Order setup required for this villager is unnecessary.";
                    return VillagerRequestResult.InvalidVillagerRequested;
                }

                sanitizedOrder = order.Substring(0, index);
                result = internalName;
                return VillagerRequestResult.Success;
            }

            public static bool IsUnadoptable(string internalName) => UnadoptableVillagers.Contains(internalName.Trim().ToLower());
        }
    }
}

using System;
using System.Collections.Concurrent;
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
    /// <summary>
    /// A command module that handles user requests for ACNH item/villager orders.
    /// Example commands: "!order" to queue items, "qc" to clear your queue, etc.
    /// </summary>
    public class Order : ModuleBase<SocketCommandContext>
    {
        // Sync object for locking queue modifications
        private static readonly object CommandSync = new();

        // Max orders in the queue at once, from config
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;

        // Optional: if you track user cooldowns
        private static readonly Dictionary<ulong, DateTime> UserLastCommand = new();

        private const string OrderItemSummary =
            "Requests the bot to add the item order to the queue based on user input. " +
            "Hex Mode: Provide item IDs in hex, separated by spaces. " +
            "Text Mode: Provide item names, separated by commas. " +
            "To parse for another language, include the language code first, followed by a comma and the items.";

        // Basic embed color for success or informational messages
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        [Command("order")]
        [Summary(OrderItemSummary)]
        public async Task RequestOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            LogUtil.LogInfo($"Order received by {Context.User.Username} - {request}", nameof(Order));

            // Attempt to parse a villager request from user input
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

            // If we do have a villager request
            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                // Check if villager injection is allowed
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

                // Overwrite "request" with the sanitized version (removes "Villager:xxx")
                request = sanitizedOrder;

                // Create a villager request object
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(
                    Context.User.Username,
                    villager,
                    0,
                    GameInfo.Strings.GetVillager(villagerName)
                );
            }

            // Attempt to parse item(s) from user text or an attached NHI file
            var itemsList = (await GetItemsFromRequestAsync(
                request,
                cfg,
                Context.Message.Attachments.FirstOrDefault()
            )).ToList();

            // If no items were found at all
            if (!itemsList.Any())
            {
                var noItemsEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ No Valid Items")
                    .WithDescription("No valid items or NHI attachment provided.")
                    .Build();
                await ReplyAsync(embed: noItemsEmbed).ConfigureAwait(false);
                return;
            }

            // Adjust item list to 40 (ACNH inventory size, typically)
            var items = AdjustItemListToTargetCount(itemsList, 40);

            // Attempt to queue the request
            await AttemptToQueueRequest(items.ToArray(), Context.User, Context.Channel, villagerRequest)
                  .ConfigureAwait(false);
        }

        /// <summary>
        /// Command for clearing any existing orders from the current user in the queue.
        /// </summary>
        [Command("qc")]
        [Summary("Clears your own orders from the queue.")]
        public async Task ClearOwnQueueAsync()
        {
            var userId = Context.User.Id;

            // Remove user's orders from the queue
            bool isCleared = ClearUserQueue(userId);

            if (isCleared)
            {
                var clearedEmbed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Queue Cleared")
                    .WithDescription($"{Context.User.Mention}, your orders have been successfully removed from the queue.")
                    .Build();
                await ReplyAsync(embed: clearedEmbed).ConfigureAwait(false);
            }
            else
            {
                var notInQueueEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ No Orders Found")
                    .WithDescription($"{Context.User.Mention}, you don't have any orders in the queue.")
                    .Build();
                await ReplyAsync(embed: notInQueueEmbed).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Removes all orders belonging to a specific user from the queue.
        /// Supports: Dictionary, ICollection, or ConcurrentQueue of IACNHOrderNotifier<Item>.
        /// </summary>
        private static bool ClearUserQueue(ulong userId)
        {
            lock (CommandSync)
            {
                // 1) If it's a Dictionary<ulong, IACNHOrderNotifier<Item>>
                if (Globals.Hub.Orders is IDictionary<ulong, IACNHOrderNotifier<Item>> ordersDictionary)
                {
                    // Find user’s orders
                    var matching = ordersDictionary.Values.Where(o => o.UserGuid == userId).ToList();
                    if (matching.Count == 0) return false;

                    foreach (var order in matching)
                    {
                        // Cancel the order gracefully
                        order.OrderCancelled(Globals.Bot, "Order canceled by user.", faulted: false);

                        // Remove from the dictionary by the order's unique ID
                        ordersDictionary.Remove(order.OrderID);
                    }
                    return true;
                }
                // 2) If it's a general ICollection (List, HashSet, etc.)
                else if (Globals.Hub.Orders is ICollection<IACNHOrderNotifier<Item>> ordersCollection)
                {
                    var matching = ordersCollection.Where(o => o.UserGuid == userId).ToList();
                    if (matching.Count == 0) return false;

                    foreach (var order in matching)
                    {
                        order.OrderCancelled(Globals.Bot, "Order canceled by user.", faulted: false);
                        ordersCollection.Remove(order);
                    }
                    return true;
                }
                // 3) If it's a ConcurrentQueue<IACNHOrderNotifier<Item>>
                else if (Globals.Hub.Orders is ConcurrentQueue<IACNHOrderNotifier<Item>> concurrentQueue)
                {
                    // We'll rebuild the queue minus the user's orders
                    var newQueue = new ConcurrentQueue<IACNHOrderNotifier<Item>>();
                    int removedCount = 0;

                    while (concurrentQueue.TryDequeue(out var order))
                    {
                        if (order.UserGuid == userId)
                        {
                            order.OrderCancelled(Globals.Bot, "Order canceled by user.", faulted: false);
                            removedCount++;
                        }
                        else
                        {
                            newQueue.Enqueue(order);
                        }
                    }

                    // Optionally, if your code can reassign the queue:
                    // (Globals.Hub as MyQueueClass).Orders = newQueue;
                    // Otherwise, copy newQueue back to the original queue:
                    while (newQueue.TryDequeue(out var item))
                    {
                        concurrentQueue.Enqueue(item);
                    }

                    return (removedCount > 0);
                }
                else
                {
                    throw new InvalidOperationException(
                        "Unsupported order collection type. Expected a Dictionary, ICollection, or ConcurrentQueue."
                    );
                }
            }
        }

        /// <summary>
        /// Ensures we have exactly <paramref name="targetCount"/> items, 
        /// trimming or duplicating random items if needed.
        /// </summary>
        private static List<Item> AdjustItemListToTargetCount(List<Item> itemsList, int targetCount = 40)
        {
            var items = itemsList.ToList(); // copy
            var random = new Random();

            // If we have more items than the target, trim
            if (items.Count > targetCount)
            {
                items = items.Take(targetCount).ToList();
            }
            // If we have fewer, randomly duplicate until we reach the target
            while (items.Count < targetCount)
            {
                items.Add(itemsList[random.Next(itemsList.Count)]);
            }

            return items;
        }

        /// <summary>
        /// Attempts to parse items from either the user’s input text or an attached NHI file.
        /// </summary>
        private static async Task<Item[]> GetItemsFromRequestAsync(
            string request,
            CrossBotConfig cfg,
            Attachment? attachment)
        {
            // If there's an attachment, try to parse it as NHI data
            if (attachment != null)
            {
                var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
                if (att.Success && att.Data is Item[] itemData)
                {
                    return itemData;
                }
            }

            // Otherwise parse from text
            return string.IsNullOrWhiteSpace(request)
                ? new[] { new Item(Item.NONE) }
                : ItemParser.GetItemsFromUserInput(
                    request,
                    cfg.DropConfig,
                    ItemDestination.FieldItemDropped
                  ).ToArray();
        }

        /// <summary>
        /// Handles all logic for adding items (and possibly a villager) to the queue,
        /// including safety checks (e.g., if orders are disabled, queue is full, etc.).
        /// </summary>
        private async Task AttemptToQueueRequest(
            IReadOnlyCollection<Item> items,
            SocketUser orderer,
            ISocketMessageChannel msgChannel,
            VillagerRequest? villagerRequest,
            bool catalogue = false)
        {
            // If orders are not accepted
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode ||
                Globals.Bot.Config.SkipConsoleBotCreation)
            {
                var unavailableEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Orders Not Accepted")
                    .WithDescription($"{Context.User.Mention}, orders are not currently accepted.")
                    .Build();
                await ReplyAsync(embed: unavailableEmbed).ConfigureAwait(false);
                return;
            }

            // If the queue is at max capacity
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

            // Check if items are "safe" (e.g., not glitch items)
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

            // Construct a MultiItem and an OrderRequest
            var multiOrder = new MultiItem(items.ToArray(), catalogue, fillToMax: true, stackMax: true);
            var requestInfo = new OrderRequest<Item>(
                multiOrder,
                items.ToArray(),
                orderer.Id,
                QueueExtensions.GetNextID(), // some method to get a new ID
                orderer,
                msgChannel,
                villagerRequest
            );

            // Notify user that their order is queued
            var queuedEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("✅ Order Queued")
                .WithDescription($"Your order has been added to the queue, {Context.User.Mention}. It will be processed soon.")
                .WithFooter("Thank you for your patience")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();
            await ReplyAsync(embed: queuedEmbed).ConfigureAwait(false);

            // Actually add the request to your queue system
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer).ConfigureAwait(false);
        }

        /// <summary>
        /// Nested class for parsing "Villager:NAME" from the user’s message, 
        /// marking certain internal names as unadoptable (special NPCs, etc.).
        /// </summary>
        public static class VillagerOrderParser
        {
            public enum VillagerRequestResult
            {
                NoVillagerRequested,
                InvalidVillagerRequested,
                Success
            }

            public static readonly List<string> UnadoptableVillagers = new()
            {
                // Existing
                "cbr18",  // Often CJ
                "der10",  // Often Daisy Mae
                "elp11",  // Often Flick

                // Additional known special or unadoptable NPCs
                "alp01", "alp02", "alp03", "alp04", "alp05",
                "cbr00", "cbr01", "cbr02", "cbr03", "cbr04", "cbr05",
                "cbr16", "cbr17", // Possibly different CJ variants
                "der00", "der01", "der02", "der03", "der04", "der11",
                "elp12", "elp13", "elp14", // Possibly Flick variants
                "hpc00", "hpc01", "hpc02", // e.g. Happy Home variants
                "isp00", "isp90",          // Isabelle variants
                "rcb00", "rco00", "rmt00", // Redd, maybe
                "owl00", "owl01",          // Blathers / Celeste
                "pge00", "pge01",          // Label & Mabel variants
                "pns00", "pns01",          // Tom Nook variants
                "hrp00", "hrp01",          // Harv or Harriet?
                "spn00", "spn01", "spn08", // K.K. Slider variants
                "wls00", "wls01",          // Wisp
                "hgh00",                   // Possibly “high” example
                "alp99", "rcb99",          // Misc placeholders

                // ... Add more if you find them in your data or want to exclude them ...
            };


            /// <summary>
            /// Checks if the user’s input contains "Villager: <internalName>" and parses it.
            /// </summary>
            public static VillagerRequestResult ExtractVillagerName(
                string order,
                out string villagerName,
                out string sanitizedOrder,
                string villagerFormat = "Villager:")
            {
                villagerName = string.Empty;
                sanitizedOrder = string.Empty;

                // Attempt to locate "Villager:" in the string
                int index = order.IndexOf(villagerFormat, StringComparison.InvariantCultureIgnoreCase);
                if (index < 0 || index + villagerFormat.Length >= order.Length)
                    return VillagerRequestResult.NoVillagerRequested;

                // Attempt to parse the internal villager name
                var internalName = order.Substring(index + villagerFormat.Length).Trim();
                var nameSearched = internalName;

                // If the name is not recognized, see if there's a local name mapping
                if (!VillagerResources.IsVillagerDataKnown(internalName))
                {
                    // Attempt to find a matching key in the dictionary
                    internalName = GameInfo.Strings.VillagerMap
                        .FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase))
                        .Key;
                }

                // If the name is in our unadoptable list
                if (IsUnadoptable(nameSearched) || IsUnadoptable(internalName))
                {
                    villagerName = $"{nameSearched} is not adoptable.";
                    return VillagerRequestResult.InvalidVillagerRequested;
                }

                // Remove "Villager:xxx" from the original order text
                sanitizedOrder = order.Substring(0, index);
                villagerName = internalName;

                return VillagerRequestResult.Success;
            }

            public static bool IsUnadoptable(string internalName)
                => UnadoptableVillagers.Contains(internalName.Trim().ToLower());
        }
    }
}

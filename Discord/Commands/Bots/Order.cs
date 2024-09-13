using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public class Order : ModuleBase<SocketCommandContext>
    {
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;
        private static readonly Dictionary<ulong, DateTime> UserLastCommand = new();
        private static readonly object CommandSync = new();

        private const string OrderItemSummary =
            "Requests the bot add the item order to the queue with the user's provided input. " +
            "Hex Mode: Item IDs (in hex); request multiple by putting spaces between items. " +
            "Text Mode: Item names; request multiple by putting commas between items. To parse for another language, include the language code first and a comma, followed by the items.";

        [Command("order")]
        [Summary(OrderItemSummary)]
        public async Task RequestOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var logMessage = $"order received by {Context.User.Username} - {request}";
            LogUtil.LogInfo(logMessage, nameof(Order));

            // Try to get villager
            var result = VillagerOrderParser.ExtractVillagerName(request, out var villagerName, out var sanitizedOrder);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {villagerName} Order has not been accepted.");
                return;
            }

            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                request = sanitizedOrder;
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(Context.User.Username, villager, 0, GameInfo.Strings.GetVillager(villagerName));
            }

            var items = await GetItemsFromRequestAsync(request, cfg, Context.Message.Attachments.FirstOrDefault());
            if (items == null)
            {
                await ReplyAsync("No valid items or NHI attachment provided!");
                return;
            }

            await AttemptToQueueRequest(items, Context.User, Context.Channel, villagerRequest).ConfigureAwait(false);
        }

        [Command("ordercat")]
        [Summary("Orders a catalogue of items created by an order tool such as ACNHMobileSpawner, does not duplicate any items.")]
        public async Task RequestCatalogueOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var logMessage = $"ordercat received by {Context.User.Username} - {request}";
            LogUtil.LogInfo(logMessage, nameof(Order));

            // Try to get villager
            var result = VillagerOrderParser.ExtractVillagerName(request, out var villagerName, out var sanitizedOrder);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {villagerName} Order has not been accepted.");
                return;
            }

            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                request = sanitizedOrder;
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(Context.User.Username, villager, 0, GameInfo.Strings.GetVillager(villagerName));
            }

            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();
            await AttemptToQueueRequest(items, Context.User, Context.Channel, villagerRequest, true).ConfigureAwait(false);
        }

        [Command("order")]
        [Summary("Requests the bot an order of items in the NHI format.")]
        public async Task RequestNHIOrderAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == null)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var downloadedData = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
            if (!downloadedData.Success || !(downloadedData.Data is Item[] items))
            {
                await ReplyAsync("Invalid NHI attachment provided!").ConfigureAwait(false);
                return;
            }

            await AttemptToQueueRequest(items, Context.User, Context.Channel, null, true).ConfigureAwait(false);
        }

        [Command("lastorder")]
        [Alias("lo", "lasto", "lorder")]
        [Summary("Requests the last order placed by the user.")]
        public async Task RequestLastOrderAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var filePath = $"UserOrder\\{Context.User.Id}.txt";

            if (!File.Exists(filePath))
            {
                await ReplyAsync($"{Context.User.Mention}, We do not have your last order logged. Please place an order and try again.").ConfigureAwait(false);
                return;
            }

            var request = File.ReadAllText(filePath);
            var result = VillagerOrderParser.ExtractVillagerName(request, out var villagerName, out var sanitizedOrder);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {villagerName} Order has not been accepted.");
                return;
            }

            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                request = sanitizedOrder;
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(Context.User.Username, villager, 0, GameInfo.Strings.GetVillager(villagerName));
            }

            var items = await GetItemsFromRequestAsync(request, cfg, Context.Message.Attachments.FirstOrDefault());
            if (items == null)
            {
                await ReplyAsync("No valid items or NHI attachment provided!");
                return;
            }

            await AttemptToQueueRequest(items, Context.User, Context.Channel, villagerRequest).ConfigureAwait(false);
        }

        [Command("checkitems")]
        [Alias("checkitem")]
        [Summary("Check the item ids to find items that will not allow an order to happen.")]
        public async Task CheckItemAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();
            var badItemsList = CheckForBadItems(request);

            if (string.IsNullOrEmpty(badItemsList))
            {
                await ReplyAsync("All items are safe to order.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"The following items are not safe to order:\n`{badItemsList}`").ConfigureAwait(false);
            }
        }

        [Command("preset")]
        [Summary("Requests the bot to order a preset created by the bot host.")]
        public async Task RequestPresetOrderAsync([Remainder] string presetName)
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var result = VillagerOrderParser.ExtractVillagerName(presetName, out var villagerName, out var sanitizedOrder);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {villagerName} Order has not been accepted.");
                return;
            }

            VillagerRequest? villagerRequest = null;
            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - Villager injection is currently disabled.");
                    return;
                }

                presetName = sanitizedOrder;
                var villager = VillagerResources.GetVillager(villagerName);
                villagerRequest = new VillagerRequest(Context.User.Username, villager, 0, GameInfo.Strings.GetVillager(villagerName));
            }

            presetName = presetName.Trim();
            var preset = PresetLoader.GetPreset(cfg.OrderConfig, presetName);
            if (preset == null)
            {
                await ReplyAsync($"{Context.User.Mention} - {presetName} is not a valid preset.");
                return;
            }

            await AttemptToQueueRequest(preset, Context.User, Context.Channel, villagerRequest, true).ConfigureAwait(false);
        }

        [Command("ListPresets")]
        [Alias("LP")]
        [Summary("Lists all the presets.")]
        public async Task RequestListPresetsAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var bot = Globals.Bot;
            var dir = new DirectoryInfo(bot.Config.OrderConfig.NHIPresetsDirectory);
            var files = dir.GetFiles("*.nhi");
            var listnhi = string.Join("\n ", files.Select(file => Path.GetFileNameWithoutExtension(file.Name)));

            await ReplyAsync($"**Presets available are the following:** {listnhi}.").ConfigureAwait(false);
        }

        [Command("uploadpreset")]
        [Alias("UpPre", "UP")]
        [Summary("Uploads a file to add to the preset folder.")]
        public async Task RequestUploadPresetAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cfg = Globals.Bot.Config;
            var attachment = Context.Message.Attachments.FirstOrDefault();

            if (attachment == null)
            {
                await ReplyAsync("No attachment provided!").ConfigureAwait(false);
                return;
            }

            var filePath = Path.Combine(cfg.OrderConfig.NHIPresetsDirectory, attachment.Filename);
            await NetUtil.DownloadFileAsync(attachment.Url, filePath).ConfigureAwait(false);

            await ReplyAsync($"Received attachment!\n\nThe following file has been added to presets folder: {attachment.Filename}");
        }

        [Command("queue")]
        [Alias("qs", "qp", "position")]
        [Summary("View your position in the queue.")]
        public async Task ViewQueuePositionAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var cooldown = Globals.Bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - This command has a {cooldown} second cooldown. Use this bot responsibly.").ConfigureAwait(false);
                return;
            }

            var position = QueueExtensions.GetPosition(Context.User.Id, out _);
            if (position < 0)
            {
                await ReplyAsync("Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            var etaMessage = position > 1
                ? $"Your predicted ETA is {QueueExtensions.GetETA(position)}."
                : "Your order will start after the current order is complete!";
            var message = $"{Context.User.Mention} - You are in the order queue. Position: {position}. {etaMessage}";

            await ReplyAsync(message).ConfigureAwait(false);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        [Command("remove")]
        [Alias("qc", "delete", "removeMe", "cancel")]
        [Summary("Remove yourself from the queue.")]
        public async Task RemoveFromQueueAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            QueueExtensions.GetPosition(Context.User.Id, out var order);
            if (order == null)
            {
                await ReplyAsync($"{Context.User.Mention} - Sorry, you are not in the queue, or your order is happening now.").ConfigureAwait(false);
                return;
            }

            order.SkipRequested = true;
            await ReplyAsync($"{Context.User.Mention} - Your order has been removed. Please note that you will not be able to rejoin the queue again for a while.").ConfigureAwait(false);
        }

        [Command("removeUser")]
        [Alias("rmu", "removeOther", "rmo")]
        [Summary("Remove someone from the queue.")]
        [RequireSudo]
        public async Task RemoveOtherFromQueueAsync(string identity)
        {
            if (IsUserBanned() || IsServerBanned()) return;

            if (!ulong.TryParse(identity, out var userId))
            {
                await ReplyAsync($"{identity} is not a valid ulong.").ConfigureAwait(false);
                return;
            }

            QueueExtensions.GetPosition(userId, out var order);
            if (order == null)
            {
                await ReplyAsync($"{identity} is not in the queue.").ConfigureAwait(false);
                return;
            }

            order.SkipRequested = true;
            await ReplyAsync($"{identity} ({order.VillagerName}) has been removed from the queue.").ConfigureAwait(false);
        }

        [Command("visitorList")]
        [Alias("visitors")]
        [Summary("Print the list of visitors on the island (dodo restore mode only).")]
        public async Task ShowVisitorList()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.Self.Owner != Context.User.Id)
            {
                await ReplyAsync($"{Context.User.Mention} - You may only view visitors in dodo restore mode. Please respect the privacy of other orderers.");
                return;
            }

            await ReplyAsync(Globals.Bot.VisitorList.VisitorFormattedString);
        }

        [Command("checkState")]
        [Alias("checkDirtyState")]
        [Summary("Prints whether or not the bot will restart the game for the next order.")]
        [RequireSudo]
        public async Task ShowDirtyStateAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("There is no order state in dodo restore mode.");
                return;
            }

            await ReplyAsync($"State: {(Globals.Bot.GameIsDirty ? "Bad" : "Good")}").ConfigureAwait(false);
        }

        [Command("queueList")]
        [Alias("ql")]
        [Summary("DMs the user the current list of names in the queue.")]
        [RequireSudo]
        public async Task ShowQueueListAsync()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("There is no queue in dodo restore mode.").ConfigureAwait(false);
                return;
            }

            try
            {
                await Context.User.SendMessageAsync($"The following users are in the queue for {Globals.Bot.TownName}: \r\n{QueueExtensions.GetQueueString()}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await ReplyAsync($"{e.Message}: Are your DMs open?").ConfigureAwait(false);
            }
        }

        [Command("gameTime")]
        [Alias("gt")]
        [Summary("Prints the last checked (current) in-game time.")]
        public async Task GetGameTime()
        {
            if (IsUserBanned() || IsServerBanned()) return;

            var bot = Globals.Bot;
            var cooldown = bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - This command has a {cooldown} second cooldown. Use this bot responsibly.").ConfigureAwait(false);
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                var nooksMessage = (bot.LastTimeState.Hour >= 22 || bot.LastTimeState.Hour < 8) ? "Nook's Cranny is closed" : "Nook's Cranny is expected to be open.";
                await ReplyAsync($"The current in-game time is: {bot.LastTimeState} \r\n{nooksMessage}").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"Last order started at: {bot.LastTimeState}").ConfigureAwait(false);
        }

        private async Task<Item[]> GetItemsFromRequestAsync(string request, CrossBotConfig cfg, Attachment? attachment)
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


        private async Task AttemptToQueueRequest(IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel, VillagerRequest? villagerRequest, bool catalogue = false)
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode || Globals.Bot.Config.SkipConsoleBotCreation)
            {
                await ReplyAsync($"{Context.User.Mention} - Orders are not currently accepted.");
                return;
            }

            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            if (GlobalBan.IsBanned(orderer.Id.ToString()))
            {
                await ReplyAsync($"{Context.User.Mention} - You have been banned for abuse. Order has not been accepted.");
                return;
            }

            if (Globals.Hub.Orders.Count >= MaxOrderCount)
            {
                await ReplyAsync($"The queue limit has been reached, there are currently {Globals.Hub.Orders.Count} players in the queue. Please try again later.").ConfigureAwait(false);
                return;
            }

            if (!InternalItemTool.CurrentInstance.IsSane(items.ToArray(), Globals.Bot.Config.DropConfig))
            {
                await ReplyAsync($"{Context.User.Mention} - You are attempting to order items that will damage your save. Order not accepted.");
                return;
            }

            if (items.Count > MultiItem.MaxOrder)
            {
                var clampedMessage = $"Users are limited to {MultiItem.MaxOrder} items per command. You've asked for {items.Count}. All items above the limit have been removed.";
                await ReplyAsync(clampedMessage).ConfigureAwait(false);
                items = items.Take(MultiItem.MaxOrder).ToArray();
            }

            var multiOrder = new MultiItem(items.ToArray(), catalogue, true);
            var requestInfo = new OrderRequest<Item>(multiOrder, items.ToArray(), orderer.Id, QueueExtensions.GetNextID(), orderer, msgChannel, villagerRequest);
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }

        private bool IsUserBanned()
        {
            if (GlobalBan.IsBanned(Context.User.Id.ToString()))
            {
                ReplyAsync($"{Context.User.Mention} - You have been banned for abuse. Order has not been accepted.").ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private bool IsServerBanned()
        {
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                Context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }
            return false;
        }

        public static bool CanCommand(ulong id, int secondsCooldown, bool addIfNotAdded)
        {
            if (secondsCooldown < 0) return true;

            lock (CommandSync)
            {
                if (UserLastCommand.ContainsKey(id))
                {
                    var inCooldownPeriod = Math.Abs((DateTime.Now - UserLastCommand[id]).TotalSeconds) < secondsCooldown;
                    if (addIfNotAdded && !inCooldownPeriod)
                    {
                        UserLastCommand[id] = DateTime.Now;
                    }
                    return !inCooldownPeriod;
                }

                if (addIfNotAdded)
                {
                    UserLastCommand.Add(id, DateTime.Now);
                }
                return true;
            }
        }

        private static string CheckForBadItems(string request)
        {
            var badItemsList = string.Empty;
            var embeddedResource = FileUtil.GetEmbeddedResource("SysBot.ACNHOrders.Resources", "InternalHexList.txt");
            var checkItems = request.Split(' ');

            foreach (var checkItem in checkItems)
            {
                if (checkItem != null && embeddedResource.Contains(checkItem))
                {
                    var itemID = ItemParser.GetID(checkItem);
                    if (itemID != Item.NONE)
                    {
                        var itemName = GameInfo.Strings.GetItemName(itemID);
                        badItemsList += $"{itemName}: {checkItem}\n";
                    }
                }
            }

            return badItemsList;
        }
    }

    public static class VillagerOrderParser
    {
        public enum VillagerRequestResult
        {
            NoVillagerRequested,
            InvalidVillagerRequested,
            Success
        }

        private static readonly List<string> UnadoptableVillagers = new()
        {
            "cbr18", "der10", "elp11", "gor11", "rbt20", "shp14", "alp", "alw", "bev", "bey", "boa",
            "boc", "bpt", "chm", "chy", "cml", "cmlb", "dga", "dgb", "doc", "dod", "fox", "fsl",
            "grf", "gsta", "gstb", "gul", "hgc", "hgh", "hgs", "kpg", "kpm", "kpp", "kps", "lom",
            "man", "mka", "mnc", "mnk", "mob", "mol", "otg", "otgb", "ott", "owl", "ows", "pck",
            "pge", "pgeb", "pkn", "plk", "plm", "plo", "poo", "poob", "pyn", "rcm", "rco", "rct",
            "rei", "seo", "skk", "slo", "spn", "sza", "szo", "tap", "tkka", "tkkb", "ttla", "ttlb",
            "tuk", "upa", "wrl", "xct"
        };

        public static VillagerRequestResult ExtractVillagerName(string order, out string result, out string sanitizedOrder, string villagerFormat = "Villager:")
        {
            result = string.Empty;
            sanitizedOrder = string.Empty;

            var index = order.IndexOf(villagerFormat, StringComparison.InvariantCultureIgnoreCase);
            if (index < 0 || index + villagerFormat.Length >= order.Length)
            {
                return VillagerRequestResult.NoVillagerRequested;
            }

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

            if (internalName == default)
            {
                result = $"{nameSearched} is not a valid internal villager name.";
                return VillagerRequestResult.InvalidVillagerRequested;
            }

            sanitizedOrder = order.Substring(0, index);
            result = internalName;
            return VillagerRequestResult.Success;
        }

        public static bool IsUnadoptable(string internalName) => UnadoptableVillagers.Contains(internalName.Trim().ToLower());
    }
}

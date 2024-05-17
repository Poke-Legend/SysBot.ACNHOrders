using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Concurrent;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext context, OrderRequest<Item> itemReq, string player, SocketUser trader)
        {
            try
            {
                const string helperMessage = "I've added you to the queue! I'll message you here when your order is ready";
                var helperEmbed = new EmbedBuilder()
                    .WithTitle("Queue Notification")
                    .WithDescription(helperMessage)
                    .WithColor(Color.Blue)
                    .Build();
                await trader.SendMessageAsync(embed: helperEmbed).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("Error")
                    .WithDescription($"{ex.HttpCode}: {ex.Reason}!")
                    .WithColor(Color.Red)
                    .Build();
                await context.Channel.SendMessageAsync(embed: errorEmbed).ConfigureAwait(false);

                var noAccessMsg = context.User == trader ? "You must enable private messages in order to be queued!" : $"{player} must enable private messages in order for them to be queued!";
                var noAccessEmbed = new EmbedBuilder()
                    .WithTitle("Private Message Disabled")
                    .WithDescription(noAccessMsg)
                    .WithColor(Color.Orange)
                    .Build();
                await context.Channel.SendMessageAsync(embed: noAccessEmbed).ConfigureAwait(false);
                return;
            }

            // Try adding
            var result = AttemptAddToQueue(itemReq, trader.Mention, trader.Username, out var msg);

            // Notify in channel with embed
            var channelEmbed = new EmbedBuilder()
                .WithTitle("Queue Update")
                .WithDescription(msg)
                .WithColor(result ? Color.Green : Color.Red)
                .Build();
            await context.Channel.SendMessageAsync(embed: channelEmbed).ConfigureAwait(false);

            // Notify in PM with embed to mirror what is said in the channel.
            var pmEmbed = new EmbedBuilder()
                .WithTitle("Queue Update")
                .WithDescription(msg)
                .WithColor(result ? Color.Green : Color.Red)
                .Build();
            await trader.SendMessageAsync(embed: pmEmbed).ConfigureAwait(false);

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!context.IsPrivate)
                    await context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
        }

        public static bool AddToQueueSync(IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg)
        {
            var result = AttemptAddToQueue(itemReq, playerMention, playerNameId, out var msge);
            msg = msge;

            return result;
        }

        private static bool AttemptAddToQueue(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray();
            var order = Array.Find(orderArray, x => x.UserGuid == itemReq.UserGuid);
            if (order != null)
            {
                if (!order.SkipRequested)
                    msg = $"{traderMention} - Sorry, you are already in the queue.";
                else
                    msg = $"{traderMention} - You have been recently removed from the queue. Please wait a while before attempting to enter the queue again.";
                return false;
            }

            if (Globals.Bot.CurrentUserName == traderDispName)
            {
                msg = $"{traderMention} - Report this error to @_hedge if you have been waiting more than 15 minutes and are still getting this error. Most likely the bot is down for everyone. Error: Failed to queue your order as it is the current processing order. Please wait a few seconds for the queue to clear if you've already completed it.";
                return false;
            }

            var position = orderArray.Length + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention} - Added you to the order queue{idToken}. Your position is: **{position}**";

            if (position > 1)
                msg += $". Your predicted ETA is {GetETA(position)}";
            else
                msg += ". Your order will start after the current order is complete!";

            if (itemReq.VillagerOrder != null)
                msg += $". {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} will be waiting for you on the island. Ensure you can collect them within the order timeframe.";

            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            var orderFound = Array.Find(orderArray, x => x.UserGuid == id);
            if (orderFound != null && !orderFound.SkipRequested)
            {
                if (orderFound is OrderRequest<Item> oreq)
                {
                    order = oreq;
                    return Array.IndexOf(orderArray, orderFound) + 1;
                }
            }

            order = null;
            return -1;
        }

        public static string GetETA(int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            int addSeconds = ArriveTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            var timeSpan = TimeSpan.FromSeconds(minSeconds + (addSeconds * (pos - 1)));
            if (timeSpan.Hours > 0)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}m:{1:D2}s", timeSpan.Minutes, timeSpan.Seconds);
        }

        private static ulong ID = 0;
        private static readonly object IDAccessor = new();
        public static ulong GetNextID()
        {
            lock (IDAccessor)
            {
                return ID++;
            }
        }

        public static void ClearQueue<T>(this ConcurrentQueue<T> queue)
        {
            while (queue.TryDequeue(out _)) { } // do nothing
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            string orderString = string.Empty;
            foreach (var ord in orderArray)
                orderString += $"{ord.VillagerName} \r\n";

            return orderString;
        }
    }
}

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
        private const int ArriveTime = 90;
        private const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext context, OrderRequest<Item> itemReq, string player, SocketUser trader)
        {
            try
            {
                var helperEmbed = CreateEmbed(
                    title: "🎉 Queue Notification",
                    description: "You've been added to the queue! I'll message you here when your order is ready.",
                    color: Color.Blue
                ).WithFooter("Thank you for your patience!");

                await trader.SendMessageAsync(embed: helperEmbed.Build()).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                var errorEmbed = CreateEmbed(
                    title: "⚠️ Error",
                    description: $"{ex.HttpCode}: {ex.Reason}!",
                    color: Color.Red
                ).WithFooter("Please try again later.");

                await context.Channel.SendMessageAsync(embed: errorEmbed.Build()).ConfigureAwait(false);

                var noAccessMsg = context.User == trader
                    ? "You must enable private messages to be queued!"
                    : $"{player} must enable private messages to be queued!";
                var noAccessEmbed = CreateEmbed(
                    title: "🔒 Private Message Disabled",
                    description: noAccessMsg,
                    color: Color.Orange
                ).WithFooter("Enable DMs in your privacy settings.");

                await context.Channel.SendMessageAsync(embed: noAccessEmbed.Build()).ConfigureAwait(false);
                return;
            }

            var result = AttemptAddToQueue(itemReq, trader.Mention, trader.Username, out var msg);
            var color = result ? Color.Green : Color.Red;
            var queueUpdateEmbed = CreateEmbed(
                title: "📋 Queue Update",
                description: msg,
                color: color
            ).WithFooter("We'll notify you when it's your turn!");

            await context.Channel.SendMessageAsync(embed: queueUpdateEmbed.Build()).ConfigureAwait(false);
            await trader.SendMessageAsync(embed: queueUpdateEmbed.Build()).ConfigureAwait(false);

            if (result && !context.IsPrivate)
            {
                await context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
        }

        public static bool AddToQueueSync(this IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg)
        {
            return AttemptAddToQueue(itemReq, playerMention, playerNameId, out msg);
        }

        private static bool AttemptAddToQueue(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg)
        {
            var orders = Globals.Hub.Orders.ToArray();
            var order = orders.FirstOrDefault(x => x.UserGuid == itemReq.UserGuid);

            if (order != null)
            {
                msg = order.SkipRequested
                    ? $"{traderMention} - You were recently removed from the queue. Please wait a while before trying again."
                    : $"{traderMention} - You are already in the queue.";
                return false;
            }

            if (Globals.Bot.CurrentUserName == traderDispName)
            {
                msg = $"{traderMention} - Error: Your order could not be queued as it is currently being processed. Please wait a few seconds for the queue to clear.";
                return false;
            }

            var position = orders.Length + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention} - You've been added to the order queue{idToken}. Your position is: **{position}**";

            if (position > 1)
                msg += $". Estimated wait time: {GetETA(position)}";
            else
                msg += ". Your order will start after the current order is complete!";

            if (itemReq.VillagerOrder != null)
                msg += $". Villager {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} will be waiting for you on the island.";

            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(this ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            var orderFound = Array.Find(orders, x => x.UserGuid == id);

            if (orderFound != null && !orderFound.SkipRequested && orderFound is OrderRequest<Item> oreq)
            {
                order = oreq;
                return Array.IndexOf(orders, orderFound) + 1;
            }

            order = null;
            return -1;
        }

        public static string GetETA(this int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            int addSeconds = ArriveTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            var timeSpan = TimeSpan.FromSeconds(minSeconds + (addSeconds * (pos - 1)));

            return timeSpan.Hours > 0
                ? $"{timeSpan.Hours:D2}h:{timeSpan.Minutes:D2}m:{timeSpan.Seconds:D2}s"
                : $"{timeSpan.Minutes:D2}m:{timeSpan.Seconds:D2}s";
        }

        public static ulong GetNextID()
        {
            lock (IDAccessor)
            {
                return ID++;
            }
        }

        public static void ClearQueue<T>(this ConcurrentQueue<T> queue)
        {
            while (queue.TryDequeue(out _)) { }
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders.ToArray().Where(x => !x.SkipRequested).Select(ord => ord.VillagerName);
            return string.Join("\r\n", orders);
        }

        private static EmbedBuilder CreateEmbed(string title, string description, Color color)
        {
            return new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(description)
                .WithColor(color)
                .WithTimestamp(DateTimeOffset.Now)
                .WithFooter(footer =>
                {
                    footer
                        .WithText("ACNH Order System")
                        .WithIconUrl("https://i.etsystatic.com/21657813/r/il/e83aef/2330819308/il_794xN.2330819308_c9ym.jpg"); // Replace with your actual icon URL
                })
                .WithThumbnailUrl("https://www.kindpng.com/picc/m/13-134663_animal-crossing-tom-nook-png-transparent-png.png"); // Replace with your actual thumbnail URL
        }

        private static ulong ID = 0;
        private static readonly object IDAccessor = new();
    }
}

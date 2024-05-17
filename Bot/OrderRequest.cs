using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public T[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }
        public bool SkipRequested { get; set; } = false;

        public OrderRequest(MultiItem data, T[] order, ulong user, ulong orderId, SocketUser trader, ISocketMessageChannel commandSentChannel, VillagerRequest? vil)
        {
            ItemOrderData = data;
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
            Order = order;
            VillagerName = trader.Username;
            VillagerOrder = vil;
        }

        private async void SendMessageWithEmbed(SocketUser user, string title, string description, string url)
        {
            var embed = new EmbedBuilder()
            {
                Color = Color.DarkBlue,
                Title = title,
                Description = description,
                ThumbnailUrl = url
            };

            await user.SendMessageAsync(embed: embed.Build());
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            SendMessageWithEmbed(Trader, "Order Cancelled", $"Oops! Something has happened with your order: {msg}", "https://media0.giphy.com/media/J63ixMPVSJJzW/giphy.gif?cid=ecf05e477363n6jj6pqcbs8x87o302xugjy344l7j1gyiypw&ep=v1_gifs_search&rid=giphy.gif&ct=g");
            if (!faulted)
                SendMessageWithEmbed(Trader, "Order Cancelled", $"{Trader.Mention} - Your order has been cancelled: {msg}", "https://media0.giphy.com/media/J63ixMPVSJJzW/giphy.gif?cid=ecf05e477363n6jj6pqcbs8x87o302xugjy344l7j1gyiypw&ep=v1_gifs_search&rid=giphy.gif&ct=g");
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            SendMessageWithEmbed(Trader, "Order Initializing", $"Your order is starting, please **ensure your inventory is __empty__**, then go talk to Orville and stay on the Dodo code entry screen. I will send you the Dodo code shortly. {msg}", "https://media1.giphy.com/media/WIwvGzMSd8jGU/giphy.gif?cid=ecf05e47k1y4kugcu7pkucoa27kx4ae7sw85igsmk66ym44y&ep=v1_gifs_related&rid=giphy.gif&ct=g");
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            SendMessageWithEmbed(Trader, "Order Ready", $"I'm waiting for you {(Trader is SocketGuildUser guildUser ? guildUser.Nickname : Trader.Username)}! {msg}. Your Dodo code is **{dodo}**", "https://media4.giphy.com/media/gwVQ7vG6KODYd8u0aK/giphy.gif?cid=ecf05e47qhgyguqq74428jh9fr8bvj7ig4phjkyeabsf1ncw&ep=v1_gifs_search&rid=giphy.gif&ct=g");
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            SendMessageWithEmbed(Trader, "Order Finished", $"Your order is complete, Thanks for your order! {msg}", "https://media0.giphy.com/media/zZsqxlIovBl9S/giphy.gif?cid=ecf05e47qhgyguqq74428jh9fr8bvj7ig4phjkyeabsf1ncw&ep=v1_gifs_search&rid=giphy.gif&ct=g");
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync(msg);
        }
    }
}

using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;
using System.Text;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        private const string CancelThumbnailUrl = "https://media0.giphy.com/media/J63ixMPVSJJzW/giphy.gif?cid=ecf05e477363n6jj6pqcbs8x87o302xugjy344l7j1gyiypw&ep=v1_gifs_search&rid=giphy.gif&ct=g";
        private const string InitThumbnailUrl = "https://media1.giphy.com/media/WIwvGzMSd8jGU/giphy.gif?cid=ecf05e47k1y4kugcu7pkucoa27kx4ae7sw85igsmk66ym44y&ep=v1_gifs_related&rid=giphy.gif&ct=g";
        private const string ReadyThumbnailUrl = "https://media4.giphy.com/media/gwVQ7vG6KODYd8u0aK/giphy.gif?cid=ecf05e47qhgyguqq74428jh9fr8bvj7ig4phjkyeabsf1ncw&ep=v1_gifs_search&rid=giphy.gif&ct=g";
        private const string FinishedThumbnailUrl = "https://media0.giphy.com/media/zZsqxlIovBl9S/giphy.gif?cid=ecf05e47qhgyguqq74428jh9fr8bvj7ig4phjkyeabsf1ncw&ep=v1_gifs_search&rid=giphy.gif&ct=g";

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

        private async void SendMessageWithEmbed(string title, string description, string? thumbnailUrl = null)
        {
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.DarkBlue)
                .WithTitle(title)
                .WithDescription(description);

            if (!string.IsNullOrEmpty(thumbnailUrl))
            {
                embedBuilder.WithThumbnailUrl(thumbnailUrl);
            }

            var embed = embedBuilder.Build();
            await Trader.SendMessageAsync(embed: embed);
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);

            var cancelMessage = $"Oops! Something has happened with your order: {msg}";
            SendMessageWithEmbed("Order Cancelled", cancelMessage, CancelThumbnailUrl);

            if (!faulted)
            {
                var mentionMessage = $"{Trader.Mention} - Your order has been cancelled: {msg}";
                SendMessageWithEmbed("Order Cancelled", mentionMessage, CancelThumbnailUrl);
            }
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            var initMessage = new StringBuilder()
                .Append("Your order is starting, please **ensure your inventory is __empty__**, ")
                .Append("then go talk to Orville and stay on the Dodo code entry screen. ")
                .Append("I will send you the Dodo code shortly. ")
                .Append(msg)
                .ToString();

            SendMessageWithEmbed("Order Initializing", initMessage, InitThumbnailUrl);
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            var readyMessage = $"I'm waiting for you {GetDisplayName()}! {msg}. Your Dodo code is **{dodo}**";
            SendMessageWithEmbed("Order Ready", readyMessage, ReadyThumbnailUrl);
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);

            var finishedMessage = $"Your order is complete, Thanks for your order! {msg}";
            SendMessageWithEmbed("Order Finished", finishedMessage, FinishedThumbnailUrl);
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync(msg);
        }

        private string GetDisplayName()
        {
            return Trader is SocketGuildUser guildUser ? guildUser.Nickname : Trader.Username;
        }
    }
}

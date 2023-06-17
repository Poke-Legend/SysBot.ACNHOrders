using Discord.Commands;
using Discord;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class PingModule : ModuleBase<SocketCommandContext>
    {
        [Command("ping")]
        [Summary("Replies with pong.")]
        public Task PingAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("Pong!")
                .WithDescription("The bot is alive.... Probably")
                .WithColor(Color.Blue)
                .WithThumbnailUrl("https://media3.giphy.com/media/eNmWr9p3AjNd0F7xWd/giphy.gif?cid=ecf05e47qz5n5vg83nak14var9ie1pfbinkki0lzuvca7xbs&ep=v1_gifs_related&rid=giphy.gif&ct=g");

            return ReplyAsync(embed: embed.Build());
        }
    }
}

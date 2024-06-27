using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using Random = System.Random;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class PingModule : ModuleBase<SocketCommandContext>
    {
        private static readonly Random random = new Random();

        private static readonly string[] images =
        {
            "https://media3.giphy.com/media/eNmWr9p3AjNd0F7xWd/giphy.gif?cid=ecf05e47qz5n5vg83nak14var9ie1pfbinkki0lzuvca7xbs&ep=v1_gifs_related&rid=giphy.gif&ct=g",
            "https://media.tenor.com/iehE0de38mkAAAAC/animal-crossing-hello.gif",
            "https://media.tenor.com/Yxp2lmU_JO0AAAAC/isabelle-animal.gif",
            "https://media.tenor.com/27tSiStpM58AAAAC/isabelle-animal-crossing.gif",
            "https://media.tenor.com/X63peV172DwAAAAM/squeakoid-animal-crossing.gif",
            "https://media.tenor.com/aMTgGRQyqBUAAAAC/animal-crossing-tom-nook.gif",
            "https://gifdb.com/images/high/animal-crossing-anime-celeste-coffee-20sjdglh60pef1hx.gif"
            // Add as many as you want...
        };

        [Command("ping")]
        [Alias("hi", "yo", "sup", "hello", "hey")]
        [Summary("Replies with pong.")]
        public async Task PingAsync()
        {
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var randomImage = images[random.Next(images.Length)];

            var embed = new EmbedBuilder()
                .WithTitle("Hi!")
                .WithDescription("The bot program is running.")
                .WithColor(Color.Blue)
                .WithImageUrl(randomImage); // This is the image that will be displayed

            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("hi")]
        [Alias("hello", "hey", "yo", "sup")]
        [Summary("Replies with pong.")]
        public async Task HiAsync()
        {
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var randomImage = images[random.Next(images.Length)];

            var embed = new EmbedBuilder()
                .WithTitle("Hi!")
                .WithDescription("The bot program is running.")
                .WithColor(Color.Blue)
                .WithImageUrl(randomImage); // This is the image that will be displayed

            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }
    }
}

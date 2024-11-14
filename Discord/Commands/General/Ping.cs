using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using System;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class Ping : ModuleBase<SocketCommandContext>
    {
        private static readonly Random RandomGenerator = new();

        private static readonly string[] PingImages =
        {
            "https://media3.giphy.com/media/eNmWr9p3AjNd0F7xWd/giphy.gif?cid=ecf05e47qz5n5vg83nak14var9ie1pfbinkki0lzuvca7xbs&ep=v1_gifs_related&rid=giphy.gif&ct=g",
            "https://media.tenor.com/iehE0de38mkAAAAC/animal-crossing-hello.gif",
            "https://media.tenor.com/Yxp2lmU_JO0AAAAC/isabelle-animal.gif",
            "https://media.tenor.com/27tSiStpM58AAAAC/isabelle-animal-crossing.gif",
            "https://media.tenor.com/X63peV172DwAAAAM/squeakoid-animal-crossing.gif",
            "https://media.tenor.com/aMTgGRQyqBUAAAAC/animal-crossing-tom-nook.gif",
            "https://gifdb.com/images/high/animal-crossing-anime-celeste-coffee-20sjdglh60pef1hx.gif"
        };

        [Command("ping")]
        [Alias("hi", "yo", "sup", "hello", "hey")]
        [Summary("Replies with pong.")]
        public async Task PingAsync()
        {
            var randomImage = GetRandomImage();

            var embed = BuildPingEmbed(randomImage);

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        private string GetRandomImage() => PingImages[RandomGenerator.Next(PingImages.Length)];

        private Embed BuildPingEmbed(string imageUrl) =>
            new EmbedBuilder()
                .WithTitle("Pong!")
                .WithDescription("The bot program is running.")
                .WithColor(Color.DarkTeal)
                .WithImageUrl(imageUrl)
                .Build();
    }
}

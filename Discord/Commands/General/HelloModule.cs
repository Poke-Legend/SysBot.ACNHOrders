using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using System;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class HelloModule : ModuleBase<SocketCommandContext>
    {
        // Static fields
        private static readonly Random _random = new();

        private static readonly string[] _images =
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

        // Command method
        [Command("hi")]
        [Alias("hello", "hey", "yo", "sup")]
        [Summary("Replies with a friendly greeting.")]
        public async Task HiAsync()
        {
            // Check if the server is banned
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            // Get a random image
            var randomImage = GetRandomImage();

            // Build and send the embed
            var embed = BuildGreetingEmbed(randomImage);
            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        // Helper method to get a random image
        private static string GetRandomImage()
        {
            return _images[_random.Next(_images.Length)];
        }

        // Helper method to build the embed
        private Embed BuildGreetingEmbed(string imageUrl)
        {
            return new EmbedBuilder()
                .WithTitle("Hi!")
                .WithDescription($"Hello, {Context.User.Mention}!")
                .WithColor(Color.DarkGreen)
                .WithImageUrl(imageUrl)
                .Build();
        }
    }
}

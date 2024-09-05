using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using System;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class DonationModule : ModuleBase<SocketCommandContext>
    {
        // Static fields
        private static readonly Random _random = new();

        private static readonly string[] _donationMessages =
        {
            "Thank you for considering a donation! Your support helps us keep the server running and improve our features.",
            "We appreciate all donations! Every contribution helps us continue offering services to the community.",
            "If you'd like to donate, please use the link below. Your support is greatly appreciated!",
            "Your donations make a big difference! Thank you for your generosity."
        };

        private static readonly string _donationLink = "https://ko-fi.com/sysbots";  // Replace with actual donation link

        // Command method
        [Command("donate")]
        [Alias("support", "contribute")]
        [Summary("Provides donation information and encourages support.")]
        public async Task DonateAsync()
        {
            // Check if the server is banned
            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            // Get a random donation message
            var randomMessage = GetRandomDonationMessage();

            // Build and send the embed
            var embed = BuildDonationEmbed(randomMessage);
            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        // Helper method to get a random donation message
        private static string GetRandomDonationMessage()
        {
            return _donationMessages[_random.Next(_donationMessages.Length)];
        }

        // Helper method to build the embed
        private static Embed BuildDonationEmbed(string message)
        {
            return new EmbedBuilder()
                .WithTitle("Support Us!")
                .WithDescription($"{message}\n\n[Click here to donate]({_donationLink})")
                .WithColor(Color.Gold)
                .WithThumbnailUrl("https://media3.giphy.com/media/eNmWr9p3AjNd0F7xWd/giphy.gif")  // Optionally replace with a donation-related image
                .Build();
        }
    }
}

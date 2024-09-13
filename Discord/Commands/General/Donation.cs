using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using System;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class Donation : ModuleBase<SocketCommandContext>
    {
        // Static fields
        private static readonly Random _random = new();
        private static readonly string _donationFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "donation.json");

        private static readonly string[] _donationMessages =
        {
            "Thank you for considering a donation! Your support helps us keep the server running and improve our features.",
            "We appreciate all donations! Every contribution helps us continue offering services to the community.",
            "If you'd like to donate, please use the link below. Your support is greatly appreciated!",
            "Your donations make a big difference! Thank you for your generosity."
        };

        // Command method to provide donation info
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

            // Ensure donation.json exists and read the donation link
            var donationLink = GetDonationLink();

            // Get a random donation message
            var randomMessage = GetRandomDonationMessage();

            // Build and send the embed
            var embed = BuildDonationEmbed(randomMessage, donationLink);
            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        // Command method to set a new donation link
        [Command("setdonation")]
        [Alias("setlink")]
        [Summary("Sets a new donation link.")]
        [RequireSudo]
        public async Task SetDonationLinkAsync(string newLink)
        {
            // Write the new link to the JSON file
            SetDonationLink(newLink);

            await ReplyAsync("Donation link updated successfully!").ConfigureAwait(false);
        }

        // Helper method to get a random donation message
        private static string GetRandomDonationMessage()
        {
            return _donationMessages[_random.Next(_donationMessages.Length)];
        }

        // Helper method to build the embed
        private static Embed BuildDonationEmbed(string message, string donationLink)
        {
            return new EmbedBuilder()
                .WithTitle("Support Us!")
                .WithDescription($"{message}\n\n[Click here to donate]({donationLink})")
                .WithColor(Color.Gold)
                .WithThumbnailUrl("https://media3.giphy.com/media/eNmWr9p3AjNd0F7xWd/giphy.gif")  // Optionally replace with a donation-related image
                .Build();
        }

        // Method to get the donation link from the JSON file
        private static string GetDonationLink()
        {
            // Ensure the file exists, if not create it with a default link
            if (!File.Exists(_donationFilePath))
            {
                var defaultLink = new DonationInfo { DonationLink = "https://ko-fi.com/sysbots" };
                var json = JsonSerializer.Serialize(defaultLink);
                File.WriteAllText(_donationFilePath, json);
            }

            // Read the donation link from the file
            var jsonData = File.ReadAllText(_donationFilePath);
            var donationInfo = JsonSerializer.Deserialize<DonationInfo>(jsonData);

            return donationInfo?.DonationLink ?? "https://ko-fi.com/sysbots";
        }

        // Method to set a new donation link and save it to the JSON file
        private static void SetDonationLink(string newLink)
        {
            var donationInfo = new DonationInfo { DonationLink = newLink };
            var json = JsonSerializer.Serialize(donationInfo);
            File.WriteAllText(_donationFilePath, json);
        }

        // Internal class to represent donation information
        private class DonationInfo
        {
            public string DonationLink { get; set; } = "https://ko-fi.com/sysbots";  // Default value
        }
    }
}

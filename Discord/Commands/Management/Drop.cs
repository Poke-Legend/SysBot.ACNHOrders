using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using NHSE.Core;
using SysBot.ACNHOrders.Discord.Helpers;
using SysBot.Base;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class Drop : ModuleBase<SocketCommandContext>
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static int MaxRequestCount => Globals.Bot.Config.DropConfig.MaxDropCount;
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        private const string DropDIYSummary =
        "Requests the bot to drop DIY recipes based on user input. " +
        "Hex Mode: Enter DIY recipe IDs in hexadecimal format, separated by spaces. " +
        "Text Mode: Enter recipe names separated by commas. Use a language code followed by a comma to parse recipes in that language.";

        private const string DropItemSummary =
        "Requests the bot to drop an item based on user input. " +
        "Hex Mode: Enter item IDs in hexadecimal format, separated by spaces. " +
        "Text Mode: Enter item names separated by commas. You can also specify a language code, followed by a comma, to parse items in that language.";

        [Command("clean")]
        [Summary("Picks up items around the bot.")]
        public async Task RequestCleanAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!Globals.Bot.Config.AllowClean)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Clean Disabled")
                    .WithDescription("The clean functionality is currently disabled.")
                    .Build();
                await ReplyAsync(embed: errorEmbed).ConfigureAwait(false);
                return;
            }

            Globals.Bot.CleanRequested = true;
            var successEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("🧹 Clean Request")
                .WithDescription("A clean request has been received and will be executed shortly.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();
            await ReplyAsync(embed: successEmbed).ConfigureAwait(false);
        }

        [Command("code")]
        [Alias("dodo")]
        [Summary("Prints the Dodo Code for the island.")]
        [RequireSudo]
        public async Task RequestDodoCodeAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var draw = Globals.Bot.DodoImageDrawer;
            var message = $"Dodo Code for **{Globals.Bot.TownName}**: **{Globals.Bot.DodoCode}**.";

            if (draw != null && draw.GetProcessedDodoImagePath() is string path)
            {
                await Context.Channel.SendFileAsync(path, message);
            }
            else
            {
                var embed = new EmbedBuilder()
                    .WithColor(EmbedColor)
                    .WithTitle("🛩️ Dodo Code")
                    .WithDescription(message)
                    .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                await ReplyAsync(embed: embed).ConfigureAwait(false);
            }
        }

        [Command("sendDodo")]
        [Alias("sd", "send")]
        [Summary("Prints the Dodo Code for the island. Only works in dodo restore mode.")]
        public async Task RequestRestoreLoopDodoAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var cfg = Globals.Bot.Config;
            if (!cfg.DodoModeConfig.AllowSendDodo && !cfg.CanUseSudo(Context.User.Id))
                return;

            if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
                return;

            var bannedUsers = await FetchBanListFromGitHubAsync();
            if (bannedUsers.Contains(Context.User.Id.ToString()))
            {
                var banEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Access Restricted")
                    .WithDescription($"{Context.User.Mention}, you are currently not allowed to use the bot. Dodo code will not be sent.")
                    .Build();
                await ReplyAsync(embed: banEmbed);
                return;
            }

            try
            {
                var message = $"Dodo Code for **{Globals.Bot.TownName}**: **{Globals.Bot.DodoCode}**.";
                await Context.User.SendMessageAsync(message);

                var confirmEmbed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Dodo Code Sent")
                    .WithDescription($"The Dodo code has been sent to {Context.User.Mention} via DM.")
                    .WithFooter("For private use only")
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                await ReplyAsync(embed: confirmEmbed).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Unable to Send Dodo Code")
                    .WithDescription($"{ex.Message}. Ensure your private messages are open to use this command.")
                    .Build();
                await ReplyAsync(embed: errorEmbed).ConfigureAwait(false);
            }
        }

        [Command("drop")]
        [Alias("dropItem")]
        [Summary("Drops a custom item (or items).")]
        public async Task RequestDropAsync([Summary(DropItemSummary)][Remainder] string request)
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, cfg.DropConfig.UseLegacyDrop ? ItemDestination.PlayerDropped : ItemDestination.HeldItem);
            MultiItem.StackToMax(items);

            await DropItems(items).ConfigureAwait(false);
        }

        [Command("dropDIY")]
        [Alias("diy")]
        [Summary("Drops a DIY recipe with the requested recipe ID(s).")]
        public async Task RequestDropDIYAsync([Summary(DropDIYSummary)][Remainder] string recipeIDs)
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var items = ItemParser.GetDIYsFromUserInput(recipeIDs);
            await DropItems(items).ConfigureAwait(false);
        }

        [Command("setTurnips")]
        [Alias("turnips")]
        [Summary("Sets all the week's turnips (minus Sunday) to a certain value.")]
        [RequireSudo]
        public async Task RequestTurnipSetAsync(int value)
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var bot = Globals.Bot;
            bot.StonkRequests.Enqueue(new TurnipRequest(Context.User.Username, value)
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"All turnip values successfully set to {value}!"
                        : "Unable to set turnip values.";
                    var embed = new EmbedBuilder()
                        .WithColor(success ? Color.Green : Color.Red)
                        .WithTitle(success ? "🌱 Turnips Set" : "❌ Turnip Set Failed")
                        .WithDescription(reply)
                        .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                        .WithTimestamp(DateTimeOffset.Now)
                        .Build();
                    Task.Run(async () => await ReplyAsync(embed: embed).ConfigureAwait(false));
                }
            });
            var queueEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("📈 Turnip Request Queued")
                .WithDescription($"Queued turnip values to be set to {value}.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();
            await ReplyAsync(embed: queueEmbed);
        }

        [Command("setTurnipsMax")]
        [Alias("turnipsMax", "stonks")]
        [Summary("Sets all the week's turnips (minus Sunday) to 999,999,999")]
        [RequireSudo]
        public async Task RequestTurnipMaxSetAsync()
        {
            await RequestTurnipSetAsync(999999999);
        }

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Invalid Item Request")
                    .WithDescription($"{Context.User.Mention}, your request contains items that may damage your save file. Request not accepted.")
                    .Build();
                await ReplyAsync(embed: errorEmbed).ConfigureAwait(false);
                return;
            }

            if (items.Count > MaxRequestCount)
            {
                var warningEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Item Limit Exceeded")
                    .WithDescription($"You are limited to {MaxRequestCount} items per command. Only the first {MaxRequestCount} items will be processed.")
                    .Build();
                await ReplyAsync(embed: warningEmbed).ConfigureAwait(false);
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(Context.User.Username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            var dropEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("📦 Item Drop Requested")
                .WithDescription($"Your item drop request will be executed shortly.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();
            await ReplyAsync(embed: dropEmbed).ConfigureAwait(false);
        }

        private async Task<bool> GetDropAvailability()
        {
            var cfg = Globals.Bot.Config;

            if (cfg.CanUseSudo(Context.User.Id) || Globals.Self.Owner == Context.User.Id)
                return true;

            if (!cfg.AllowDrop)
            {
                var disabledEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Drop Disabled")
                    .WithDescription("Item drop functionality is currently disabled.")
                    .Build();
                await ReplyAsync(embed: disabledEmbed).ConfigureAwait(false);
                return false;
            }

            if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                var restrictionEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Drop Restricted")
                    .WithDescription($"{Context.User.Mention}, you are only permitted to use this command while on the island during your order.")
                    .Build();
                await ReplyAsync(embed: restrictionEmbed).ConfigureAwait(false);
                return false;
            }

            return true;
        }
        private async Task<List<string>> FetchBanListFromGitHubAsync()
        {
            try
            {
                // Fetch the JSON data from GitHub
                var response = await HttpClient.GetStringAsync("https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/userban.json");
                var jsonData = JsonConvert.DeserializeObject<GitHubFileContent>(response);

                // Check if jsonData or jsonData.Content is null
                if (jsonData?.Content == null)
                {
                    Console.WriteLine("Error: Content is null in the GitHub file response.");
                    return new List<string>();
                }

                // Decode the base64 content to get the actual JSON data
                var decodedJson = Encoding.UTF8.GetString(Convert.FromBase64String(jsonData.Content));
                var bannedUsers = JsonConvert.DeserializeObject<List<string>>(decodedJson);

                return bannedUsers ?? new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching ban list from GitHub: {ex.Message}");
                return new List<string>(); // Return empty list if there's an error
            }
        }

        private class GitHubFileContent
        {
            [JsonProperty("content")]
            public string Content { get; set; } = string.Empty; // Default to an empty string to satisfy non-nullable requirement
        }
    }
}



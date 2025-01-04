using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;
using NHSE.Villagers;
using static SysBot.ACNHOrders.Order;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Bots
{
    /// <summary>
    /// Module for managing villagers in the Discord bot.
    /// </summary>
    public class Villager : ModuleBase<SocketCommandContext>
    {
        #region Commands

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        public async Task InjectVillagerAsync(int index, string internalName)
        {
            await InjectVillagersAsync(index, new[] { internalName });
        }

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        public async Task InjectVillagerAsync(string internalName)
        {
            await InjectVillagerAsync(0, internalName).ConfigureAwait(false);
        }

        [Command("multiVillager"), Alias("mvi", "injectVillagerMulti", "superUltraInjectionGiveMeMoreVillagers")]
        [Summary("Injects multiple villagers based on the internal names.")]
        public async Task InjectVillagerMultiAsync([Remainder] string names)
        {
            var villagerNames = names.Split(new[] { ",", " " }, StringSplitOptions.RemoveEmptyEntries);
            await InjectVillagersAsync(0, villagerNames);
        }

        [Command("villagers"), Alias("vl", "villagerList")]
        [Summary("Prints the list of villagers currently on the island.")]
        public async Task GetVillagerListAsync()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyErrorAsync("Villagers on the island may be replaceable by adding them to your order command.");
                return;
            }

            var villagers = Globals.Bot.Villagers.LastVillagers;
            var embed = new EmbedBuilder()
                .WithTitle($"🌴 Villagers on {Globals.Bot.TownName}")
                .WithDescription($"{Context.User.Mention}, here are the villagers currently on the island:\n{villagers}.")
                .WithColor(Color.Green)
                .WithFooter("Thank you for using Villager Bot!")
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        public async Task GetVillagerInternalNameAsync(string language, [Remainder] string villagerName)
        {
            var strings = GameInfo.GetStrings(language);
            await ReplyVillagerNameAsync(strings, villagerName).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        public async Task GetVillagerInternalNameAsync([Remainder] string villagerName)
        {
            var strings = GameInfo.Strings;
            await ReplyVillagerNameAsync(strings, villagerName).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        private async Task ReplyErrorAsync(string message)
        {
            var embed = new EmbedBuilder()
                .WithTitle("❌ Error")
                .WithDescription($"{Context.User.Mention} - {message}")
                .WithColor(Color.Red)
                .WithFooter("Please check command permissions or try again later.")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        private async Task InjectVillagersAsync(int startIndex, string[] villagerNames)
        {
            if (!Globals.Bot.Config.AllowVillagerInjection)
            {
                await ReplyErrorAsync("Villager injection is currently disabled.");
                return;
            }

            if (villagerNames.Length < 1)
            {
                await ReplyErrorAsync("No villager names provided in command.");
                return;
            }

            int index = startIndex;
            int count = villagerNames.Length;

            foreach (var name in villagerNames)
            {
                var internalName = ResolveInternalName(name);
                if (internalName == null)
                {
                    await ReplyErrorAsync($"{name} is not a valid internal villager name.");
                    return;
                }

                if (!IsValidIndex(index))
                {
                    await ReplyErrorAsync($"{index} is not a valid index.");
                    return;
                }

                EnqueueVillagerInjection(internalName, index);
                index = (index + 1) % 10;
            }

            var addMsg = count > 1 ? $"Villager inject request for {count} villagers has" : "Villager inject request has";
            var msg = $"{Context.User.Mention}, {addMsg} been added to the queue and will be injected momentarily. I will reply to you once this is completed.";
            var embedResponse = new EmbedBuilder()
                .WithTitle("🌸 Villager Injection")
                .WithDescription(msg)
                .WithColor(Color.Blue)
                .WithFooter("Thank you for your patience!")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: embedResponse).ConfigureAwait(false);
        }

        private string ResolveInternalName(string nameLookup)
        {
            if (VillagerResources.IsVillagerDataKnown(nameLookup))
                return nameLookup;

            return GameInfo.Strings.VillagerMap
                .FirstOrDefault(z => string.Equals(z.Value, nameLookup, StringComparison.InvariantCultureIgnoreCase))
                .Key;
        }

        private bool IsValidIndex(int index)
        {
            return index >= 0 && index <= byte.MaxValue;
        }

        private void EnqueueVillagerInjection(string internalName, int index)
        {
            var replace = VillagerResources.GetVillager(internalName);
            // Check if the villager is in the "unadoptable" list (per your Order/VillagerOrderParser)
            var extraMsg = VillagerOrderParser.IsUnadoptable(internalName)
                ? " Please note that you will not be able to adopt this villager."
                : string.Empty;

            var request = new VillagerRequest(
                Context.User.Username,
                replace,
                (byte)index,
                GameInfo.Strings.GetVillager(internalName)
            )
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"{GameInfo.Strings.GetVillager(internalName)} has been injected by the bot at Index {index}. Please go talk to them!{extraMsg}"
                        : "Failed to inject villager. Please tell the bot owner to look at the logs!";

                    var embed = new EmbedBuilder()
                        .WithTitle("🌸 Villager Injection Status")
                        .WithDescription($"{Context.User.Mention}: {reply}")
                        .WithColor(success ? Color.Green : Color.Red)
                        .WithFooter(success ? "Villager injected successfully!" : "Injection failed.")
                        .WithTimestamp(DateTimeOffset.Now)
                        .Build();

                    Task.Run(async () => await ReplyAsync(embed: embed).ConfigureAwait(false));
                }
            };

            Globals.Bot.VillagerInjections.Enqueue(request);
        }

        private async Task ReplyVillagerNameAsync(GameStrings strings, string villagerName)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyErrorAsync("Lookup commands are not accepted.");
                return;
            }

            var lookupKey = villagerName.Replace(" ", string.Empty);
            var result = strings.VillagerMap
                .FirstOrDefault(z => string.Equals(lookupKey, z.Value, StringComparison.InvariantCultureIgnoreCase));

            if (string.IsNullOrWhiteSpace(result.Key))
            {
                var embed = new EmbedBuilder()
                    .WithTitle("🌸 Villager Lookup")
                    .WithDescription($"No villager found with the name {villagerName}.")
                    .WithColor(Color.Red)
                    .WithFooter("Try another name or check spelling.")
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();
                await ReplyAsync(embed: embed).ConfigureAwait(false);
                return;
            }

            var nameEmbed = new EmbedBuilder()
                .WithTitle("🌸 Villager Lookup")
                .WithDescription($"{villagerName} = {result.Key}")
                .WithColor(Color.Green)
                .WithFooter("Internal name found successfully!")
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: nameEmbed).ConfigureAwait(false);
        }

        #endregion
    }
}

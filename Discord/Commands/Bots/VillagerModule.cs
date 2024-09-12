using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders.Discord.Commands.Bots
{
    /// <summary>
    /// Module for managing villagers in the Discord bot.
    /// </summary>
    public class VillagerModule : ModuleBase<SocketCommandContext>
    {
        #region Commands

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        public async Task InjectVillagerAsync(int index, string internalName)
        {
            if (!await PreExecuteCheckAsync()) return;

            await InjectVillagersAsync(index, new[] { internalName });
        }

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        public async Task InjectVillagerAsync(string internalName)
        {
            if (!await PreExecuteCheckAsync()) return;

            await InjectVillagerAsync(0, internalName).ConfigureAwait(false);
        }

        [Command("multiVillager"), Alias("mvi", "injectVillagerMulti", "superUltraInjectionGiveMeMoreVillagers")]
        [Summary("Injects multiple villagers based on the internal names.")]
        public async Task InjectVillagerMultiAsync([Remainder] string names)
        {
            if (!await PreExecuteCheckAsync()) return;

            var villagerNames = names.Split(new[] { ",", " " }, StringSplitOptions.RemoveEmptyEntries);
            await InjectVillagersAsync(0, villagerNames);
        }

        [Command("villagers"), Alias("vl", "villagerList")]
        [Summary("Prints the list of villagers currently on the island.")]
        public async Task GetVillagerListAsync()
        {
            if (!await PreExecuteCheckAsync()) return;

            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyErrorAsync("Villagers on the island may be replaceable by adding them to your order command.");
                return;
            }

            var villagers = Globals.Bot.Villagers.LastVillagers;
            var embed = new EmbedBuilder()
                .WithTitle($"Villagers on {Globals.Bot.TownName}")
                .WithDescription($"{Context.User.Mention}: {villagers}.")
                .WithColor(Color.Green)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        public async Task GetVillagerInternalNameAsync(string language, [Remainder] string villagerName)
        {
            if (!await PreExecuteCheckAsync()) return;

            var strings = GameInfo.GetStrings(language);
            await ReplyVillagerNameAsync(strings, villagerName).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        public async Task GetVillagerInternalNameAsync([Remainder] string villagerName)
        {
            if (!await PreExecuteCheckAsync()) return;

            var strings = GameInfo.Strings;
            await ReplyVillagerNameAsync(strings, villagerName).ConfigureAwait(false);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Performs pre-execution checks like server ban and configuration validations.
        /// </summary>
        /// <returns>True if execution should continue; otherwise, false.</returns>
        private async Task<bool> PreExecuteCheckAsync()
        {
            if (IsServerBanned())
            {
                await LeaveGuildAsync();
                return false;
            }

            if (!Globals.Bot.Config.AllowVillagerInjection)
            {
                await ReplyErrorAsync("Villager injection is currently disabled.");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the server is banned.
        /// </summary>
        /// <returns>True if banned; otherwise, false.</returns>
        private bool IsServerBanned()
        {
            return GlobalBan.IsServerBanned(Context.Guild.Id.ToString());
        }

        /// <summary>
        /// Leaves the guild asynchronously.
        /// </summary>
        private async Task LeaveGuildAsync()
        {
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Sends an error embed message.
        /// </summary>
        /// <param name="message">Error message to display.</param>
        private async Task ReplyErrorAsync(string message)
        {
            var embed = new EmbedBuilder()
                .WithTitle("Error")
                .WithDescription($"{Context.User.Mention} - {message}")
                .WithColor(Color.Red)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        /// <summary>
        /// Injects villagers based on provided names.
        /// </summary>
        /// <param name="startIndex">Starting index for injection.</param>
        /// <param name="villagerNames">Array of villager internal names.</param>
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
            var msg = $"{Context.User.Mention}: {addMsg} been added to the queue and will be injected momentarily. I will reply to you once this has completed.";
            var embedResponse = new EmbedBuilder()
                .WithTitle("Villager Injection")
                .WithDescription(msg)
                .WithColor(Color.Blue)
                .Build();

            await ReplyAsync(embed: embedResponse).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves the internal name of a villager.
        /// </summary>
        /// <param name="nameLookup">The name to lookup.</param>
        /// <returns>Resolved internal name or null if not found.</returns>
        private string ResolveInternalName(string nameLookup)
        {
            if (VillagerResources.IsVillagerDataKnown(nameLookup))
                return nameLookup;

            return GameInfo.Strings.VillagerMap
                .FirstOrDefault(z => string.Equals(z.Value, nameLookup, StringComparison.InvariantCultureIgnoreCase))
                .Key;
        }

        /// <summary>
        /// Checks if the provided index is valid.
        /// </summary>
        /// <param name="index">Index to validate.</param>
        /// <returns>True if valid; otherwise, false.</returns>
        private bool IsValidIndex(int index)
        {
            return index >= 0 && index <= byte.MaxValue;
        }

        /// <summary>
        /// Enqueues a villager injection request.
        /// </summary>
        /// <param name="internalName">Internal name of the villager.</param>
        /// <param name="index">Index at which to inject.</param>
        private void EnqueueVillagerInjection(string internalName, int index)
        {
            var replace = VillagerResources.GetVillager(internalName);
            var extraMsg = VillagerOrderParser.IsUnadoptable(internalName)
                ? " Please note that you will not be able to adopt this villager."
                : string.Empty;

            var request = new VillagerRequest(Context.User.Username, replace, (byte)index, GameInfo.Strings.GetVillager(internalName))
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"{GameInfo.Strings.GetVillager(internalName)} has been injected by the bot at Index {index}. Please go talk to them!{extraMsg}"
                        : "Failed to inject villager. Please tell the bot owner to look at the logs!";

                    var embed = new EmbedBuilder()
                        .WithTitle("Villager Injection")
                        .WithDescription($"{Context.User.Mention}: {reply}")
                        .WithColor(success ? Color.Green : Color.Red)
                        .Build();

                    Task.Run(async () => await ReplyAsync(embed: embed).ConfigureAwait(false));
                }
            };

            Globals.Bot.VillagerInjections.Enqueue(request);
        }

        /// <summary>
        /// Replies with the internal name of a villager.
        /// </summary>
        /// <param name="strings">Game strings based on language.</param>
        /// <param name="villagerName">Name of the villager to lookup.</param>
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
                    .WithTitle("Villager Lookup")
                    .WithDescription($"No villager found with the name {villagerName}.")
                    .WithColor(Color.Red)
                    .Build();
                await ReplyAsync(embed: embed).ConfigureAwait(false);
                return;
            }

            var nameEmbed = new EmbedBuilder()
                .WithTitle("Villager Lookup")
                .WithDescription($"{villagerName} = {result.Key}")
                .WithColor(Color.Green)
                .Build();

            await ReplyAsync(embed: nameEmbed).ConfigureAwait(false);
        }
        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class OwnerModule : ModuleBase<SocketCommandContext>
    {
        private const int GuildsPerPage = 25;
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        [Command("leave")]
        [Alias("bye")]
        [Summary("Removes the current channel's ID from the GitHub channel list.")]
        [RequireOwner]
        public async Task LeaveAsync()
        {
            ulong channelIdToRemove = Context.Channel.Id;

            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to fetch channel list from GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            if (channelIds.Contains(channelIdToRemove))
            {
                channelIds.Remove(channelIdToRemove);
                string updatedContent = JsonConvert.SerializeObject(channelIds);
                string commitMessage = $"Removed channel ID {channelIdToRemove} from channel list.";

                var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
                if (fileSha == null)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("❌ Error")
                        .WithDescription("Failed to retrieve file SHA from GitHub.");
                    await ReplyAsync(embed: errorEmbed.Build());
                    return;
                }

                bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
                if (!updateSuccessful)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("❌ Error")
                        .WithDescription("Failed to update channel list on GitHub.");
                    await ReplyAsync(embed: errorEmbed.Build());
                    return;
                }

                var successEmbed = new EmbedBuilder()
                    .WithColor(EmbedColor)
                    .WithTitle("👋 Goodbye")
                    .WithDescription($"Channel ID {channelIdToRemove} has been removed from the GitHub list.");
                await ReplyAsync(embed: successEmbed.Build());
            }
            else
            {
                var notFoundEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Not Found")
                    .WithDescription("Channel ID not found in the GitHub list.");
                await ReplyAsync(embed: notFoundEmbed.Build());
            }
        }

        [Command("leaveall")]
        [Summary("Removes all channels the bot is currently in from the GitHub channel list.")]
        [RequireSudo]
        public async Task LeaveAllAsync()
        {
            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to fetch channel list from GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            var botChannelIds = Context.Client.Guilds
                .SelectMany(guild => guild.TextChannels)
                .Select(channel => channel.Id)
                .ToList();

            var updatedChannelIds = channelIds.Except(botChannelIds).ToList();
            string updatedContent = JsonConvert.SerializeObject(updatedChannelIds);
            string commitMessage = "Removed all bot channel IDs from the GitHub channel list.";

            var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
            if (fileSha == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to retrieve file SHA from GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
            if (!updateSuccessful)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to update channel list on GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            var successEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("👋 Goodbye")
                .WithDescription("All bot channel IDs have been removed from the GitHub channel list.");
            await ReplyAsync(embed: successEmbed.Build());
        }

        [Command("leaveguild")]
        [Summary("Leaves the specified server by ID and removes a specific channel's ID from that server on the GitHub channel list.")]
        [RequireSudo]
        public async Task LeaveGuildAsync(ulong guildId)
        {
            var guild = Context.Client.GetGuild(guildId);
            if (guild == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription($"Could not find guild with ID: {guildId}. Ensure the bot is currently a member of this server.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            var channelToRemove = guild.DefaultChannel ?? guild.TextChannels.FirstOrDefault();
            if (channelToRemove == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("No suitable channel found in the specified guild to remove from GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            ulong channelIdToRemove = channelToRemove.Id;
            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Error")
                    .WithDescription("Failed to fetch channel list from GitHub.");
                await ReplyAsync(embed: errorEmbed.Build());
                return;
            }

            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            if (channelIds.Contains(channelIdToRemove))
            {
                channelIds.Remove(channelIdToRemove);
                string updatedContent = JsonConvert.SerializeObject(channelIds);
                string commitMessage = $"Removed channel ID {channelIdToRemove} from channel list for guild {guild.Name}";

                var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
                if (fileSha == null)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("❌ Error")
                        .WithDescription("Failed to retrieve file SHA from GitHub.");
                    await ReplyAsync(embed: errorEmbed.Build());
                    return;
                }

                bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
                if (!updateSuccessful)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithTitle("❌ Error")
                        .WithDescription("Failed to update channel list on GitHub.");
                    await ReplyAsync(embed: errorEmbed.Build());
                    return;
                }

                var successEmbed = new EmbedBuilder()
                    .WithColor(EmbedColor)
                    .WithTitle("✅ Channel Removed")
                    .WithDescription($"Channel ID {channelIdToRemove} has been removed from the GitHub list for guild {guild.Name}.");
                await ReplyAsync(embed: successEmbed.Build());
            }
            else
            {
                var notFoundEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ Not Found")
                    .WithDescription("Channel ID not found in the GitHub list.");
                await ReplyAsync(embed: notFoundEmbed.Build());
            }

            var leaveEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("👋 Leaving Guild")
                .WithDescription($"Leaving server: {guild.Name} (ID: {guild.Id})");
            await ReplyAsync(embed: leaveEmbed.Build());
            await guild.LeaveAsync();
        }

        [Command("addSudo")]
        [RequireOwner]
        public async Task AddSudoAsync([Remainder] string userInput)
        {
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                user = Context.Client.GetUser(userId);
            }

            if (user == null)
            {
                foreach (var guild in Context.Client.Guilds)
                {
                    var guildUser = guild.Users.FirstOrDefault(u => u.Username.Equals(userInput, StringComparison.OrdinalIgnoreCase));
                    if (guildUser != null)
                    {
                        user = guildUser;
                        break;
                    }
                }
            }

            if (user == null)
            {
                var notFoundEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ User Not Found")
                    .WithDescription("Please mention a valid user, provide a valid user ID, or enter a valid username.");
                await ReplyAsync(embed: notFoundEmbed.Build());
                return;
            }

            await Globals.Manager.AddSudoAsync(user.Id);
            var successEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("✅ Sudo Added")
                .WithDescription($"{user.Username} has been added to the sudo list.");
            await ReplyAsync(embed: successEmbed.Build());
        }

        [Command("removeSudo")]
        [RequireOwner]
        public async Task RemoveSudoAsync([Remainder] string userInput)
        {
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                user = Context.Client.GetUser(userId);
            }

            if (user == null)
            {
                foreach (var guild in Context.Client.Guilds)
                {
                    var guildUser = guild.Users.FirstOrDefault(u => u.Username.Equals(userInput, StringComparison.OrdinalIgnoreCase));
                    if (guildUser != null)
                    {
                        user = guildUser;
                        break;
                    }
                }
            }

            if (user == null)
            {
                var notFoundEmbed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⚠️ User Not Found")
                    .WithDescription("Please mention a valid user, provide a valid user ID, or enter a valid username.");
                await ReplyAsync(embed: notFoundEmbed.Build());
                return;
            }

            await Globals.Manager.RemoveSudoAsync(user.Id);
            var successEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("✅ Sudo Removed")
                .WithDescription($"{user.Username} has been removed from the sudo list.");
            await ReplyAsync(embed: successEmbed.Build());
        }

        [Command("listSudo")]
        [RequireOwner]
        public async Task ListSudoAsync()
        {
            var sudoUsers = Globals.Manager.GetAllSudoUsers();
            if (!sudoUsers.Any())
            {
                var noSudoEmbed = new EmbedBuilder()
                    .WithColor(EmbedColor)
                    .WithTitle("📝 Sudo List")
                    .WithDescription("No users have sudo privileges.");
                await ReplyAsync(embed: noSudoEmbed.Build());
                return;
            }

            var userList = string.Join("\n", sudoUsers.Select(id => $"<@{id}>"));
            var sudoListEmbed = new EmbedBuilder()
                .WithColor(EmbedColor)
                .WithTitle("📝 Sudo List")
                .WithDescription(userList);
            await ReplyAsync(embed: sudoListEmbed.Build());
        }
    }
}

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
            // Get the current channel ID
            ulong channelIdToRemove = Context.Channel.Id;

            // Fetch the existing list of channel IDs from GitHub
            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                await ReplyAsync("Failed to fetch channel list from GitHub.");
                return;
            }

            // Deserialize the list of channel IDs
            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            // Remove the current channel ID if it exists in the list
            if (channelIds.Contains(channelIdToRemove))
            {
                channelIds.Remove(channelIdToRemove);

                // Prepare the updated content for GitHub
                string updatedContent = JsonConvert.SerializeObject(channelIds);
                string commitMessage = $"Removed channel ID {channelIdToRemove} from channel list.";

                // Fetch the file's SHA to allow updating
                var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
                if (fileSha == null)
                {
                    await ReplyAsync("Failed to retrieve file SHA from GitHub.");
                    return;
                }

                // Attempt to update the file on GitHub
                bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
                if (!updateSuccessful)
                {
                    await ReplyAsync("Failed to update channel list on GitHub.");
                    return;
                }

                await ReplyAsync($"GoodBye!");
            }
            else
            {
                await ReplyAsync("Channel ID not found in the GitHub list.");
            }
        }

        [Command("leaveall")]
        [Summary("Removes all channels the bot is currently in from the GitHub channel list.")]
        [RequireSudo]  // Change from RequireOwner to RequireSudo if you have specific IDs controlling this
        public async Task LeaveAllAsync()
        {
            // Fetch the existing list of channel IDs from GitHub
            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                await ReplyAsync("Failed to fetch channel list from GitHub.");
                return;
            }

            // Deserialize the list of channel IDs from GitHub
            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            // Get all channel IDs where the bot is present across all guilds
            var botChannelIds = Context.Client.Guilds
                .SelectMany(guild => guild.TextChannels) // Get all text channels
                .Select(channel => channel.Id)
                .ToList();

            // Remove each bot channel ID from the GitHub list if it exists
            var updatedChannelIds = channelIds.Except(botChannelIds).ToList();

            // Prepare the updated content for GitHub
            string updatedContent = JsonConvert.SerializeObject(updatedChannelIds);
            string commitMessage = "Removed all bot channel IDs from the GitHub channel list.";

            // Fetch the file's SHA to allow updating
            var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
            if (fileSha == null)
            {
                await ReplyAsync("Failed to retrieve file SHA from GitHub.");
                return;
            }

            // Attempt to update the file on GitHub
            bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
            if (!updateSuccessful)
            {
                await ReplyAsync("Failed to update channel list on GitHub.");
                return;
            }

            await ReplyAsync("GoodBye!");
        }

        [Command("leaveguild")]
        [Summary("Leaves the specified server by ID and removes a specific channel's ID from that server on the GitHub channel list.")]
        [RequireSudo]
        public async Task LeaveGuildAsync(ulong guildId)
        {
            // Step 1: Attempt to get the guild
            var guild = Context.Client.GetGuild(guildId);
            if (guild == null)
            {
                await ReplyAsync($"Could not find guild with ID: {guildId}. Ensure the bot is currently a member of this server.");
                return;
            }

            // Step 2: Get the ID of a specific channel within the guild to remove from GitHub
            // Here, we'll assume the "general" channel or the first available text channel
            var channelToRemove = guild.DefaultChannel ?? guild.TextChannels.FirstOrDefault();
            if (channelToRemove == null)
            {
                await ReplyAsync("No suitable channel found in the specified guild to remove from GitHub.");
                return;
            }

            ulong channelIdToRemove = channelToRemove.Id;

            // Step 3: Fetch the existing list of channel IDs from GitHub
            var channelListJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            if (channelListJson == null)
            {
                await ReplyAsync("Failed to fetch channel list from GitHub.");
                return;
            }

            // Deserialize the list of channel IDs
            var channelIds = JsonConvert.DeserializeObject<List<ulong>>(channelListJson) ?? new List<ulong>();

            // Step 4: Remove the channel ID if it exists in the list
            if (channelIds.Contains(channelIdToRemove))
            {
                channelIds.Remove(channelIdToRemove);

                // Prepare the updated content for GitHub
                string updatedContent = JsonConvert.SerializeObject(channelIds);
                string commitMessage = $"Removed channel ID {channelIdToRemove} from channel list for guild {guild.Name}";

                // Fetch the file's SHA to allow updating
                var fileSha = await GitHubApi.GetFileShaAsync(GitHubApi.ChannelListApiUrl);
                if (fileSha == null)
                {
                    await ReplyAsync("Failed to retrieve file SHA from GitHub.");
                    return;
                }

                // Attempt to update the file on GitHub
                bool updateSuccessful = await GitHubApi.UpdateFileAsync(updatedContent, commitMessage, GitHubApi.ChannelListApiUrl, fileSha);
                if (!updateSuccessful)
                {
                    await ReplyAsync("Failed to update channel list on GitHub.");
                    return;
                }

                await ReplyAsync($"Channel ID {channelIdToRemove} has been removed from the GitHub list.");
            }
            else
            {
                await ReplyAsync("Channel ID not found in the GitHub list.");
            }

            // Step 5: Leave the specified guild
            await ReplyAsync($"Leaving server: {guild.Name} (ID: {guild.Id})");
            await guild.LeaveAsync();
        }


        [Command("addSudo")]
        [RequireOwner]  // Only allow the bot owner to add sudo users
        public async Task AddSudoAsync([Remainder] string userInput)
        {
            // Try to find the user by mention, ID, or username
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                // Try to get the user by ID
                user = Context.Client.GetUser(userId);
            }

            if (user == null)
            {
                // Iterate through each guild and look for the user by username
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

            // If still no user found, return an error message
            if (user == null)
            {
                await ReplyAsync("User not found. Please mention a valid user, provide a valid user ID, or enter a valid username.");
                return;
            }

            // Add the user to the sudo list and save
            await Globals.Manager.AddSudoAsync(user.Id);
            await ReplyAsync($"{user.Username} has been added to the sudo list.").ConfigureAwait(false);
        }

        [Command("removeSudo")]
        [RequireOwner]  // Only allow the bot owner to remove sudo users
        public async Task RemoveSudoAsync([Remainder] string userInput)
        {
            // Try to find the user by mention, ID, or username
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                // Try to get the user by ID
                user = Context.Client.GetUser(userId);
            }

            if (user == null)
            {
                // Iterate through each guild and look for the user by username
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

            // If still no user found, return an error message
            if (user == null)
            {
                await ReplyAsync("User not found. Please mention a valid user, provide a valid user ID, or enter a valid username.");
                return;
            }

            // Remove the user from the sudo list and save
            await Globals.Manager.RemoveSudoAsync(user.Id);
            await ReplyAsync($"{user.Username} has been removed from the sudo list.").ConfigureAwait(false);
        }

        [Command("listSudo")]
        [RequireOwner]  // Only allow the bot owner to view the sudo list
        public async Task ListSudoAsync()
        {
            var sudoUsers = Globals.Manager.GetAllSudoUsers();
            if (!sudoUsers.Any())
            {
                await ReplyAsync("No users have sudo privileges.").ConfigureAwait(false);
                return;
            }

            var userList = string.Join("\n", sudoUsers.Select(id => $"<@{id}>"));
            await ReplyAsync($"Sudo users:\n{userList}").ConfigureAwait(false);
        }

        // Helper method to get a user reference
        private SudoUser GetReference(SocketUser user)
        {
            return new SudoUser
            {
                ID = user.Id,
                Username = user.Username // This ensures the Username is always initialized
            };
        }

        // Helper method to reply and delete the message after a delay
        private async Task ReplyAndDeleteAsync(string message)
        {
            var userMessage = await ReplyAsync(message);
            await Task.Delay(2000);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        }
    }

    public class SudoUser
    {
        public ulong ID { get; set; }
        public string Username { get; set; } = string.Empty; // Initialize with default value
    }
}

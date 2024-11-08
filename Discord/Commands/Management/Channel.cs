using Discord.Commands;
using Discord;
using System.Threading.Tasks;
using SysBot.ACNHOrders.Discord.Helpers;

namespace SysBot.ACNHOrders.Discord.Commands.Helpers
{
    public class Channel : ModuleBase<SocketCommandContext>
    {
        [Command("addchannel")]
        [Summary("Adds the current channel to the list of channels for status updates.")]
        [RequireSudo]
        public async Task AddChannelAsync()
        {
            // Check if the server is banned
            if (GlobalBan.IsServerBannedAsync(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            // Get the current channel ID
            var channelId = Context.Channel.Id;

            // Load the existing channels from file using ChannelManager
            var channelManager = new ChannelManager();
            var availableChannels = channelManager.LoadChannels();

            // If channel is already in the list, inform the user
            if (availableChannels.Contains(channelId))
            {
                await ReplyAsync($"This channel is already in the list.").ConfigureAwait(false);
                return;
            }

            // Add the channel to the list
            availableChannels.Add(channelId);

            // Save the updated list back to the file using ChannelManager
            channelManager.SaveChannels(availableChannels);

            // Notify the user
            await ReplyAsync($"Channel {Context.Channel.Name} added to the list.").ConfigureAwait(false);
        }

        [Command("removechannel")]
        [Summary("Removes the current channel from the list of channels for status updates.")]
        [RequireSudo]
        public async Task RemoveChannelAsync()
        {
            // Check if the server is banned
            if (GlobalBan.IsServerBannedAsync(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            // Get the current channel ID
            var channelId = Context.Channel.Id;

            // Load the existing channels from file using ChannelManager
            var channelManager = new ChannelManager();
            var availableChannels = channelManager.LoadChannels();

            // If the channel is not in the list, inform the user
            if (!availableChannels.Contains(channelId))
            {
                await ReplyAsync($"This channel is not in the list.").ConfigureAwait(false);
                return;
            }

            // Remove the channel from the list
            availableChannels.Remove(channelId);

            // Save the updated list back to the file using ChannelManager
            channelManager.SaveChannels(availableChannels);

            // Notify the user
            await ReplyAsync($"Channel {Context.Channel.Name} removed from the list.").ConfigureAwait(false);
        }
    }
}

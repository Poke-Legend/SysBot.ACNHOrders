using Discord.Commands;
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
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            if (availableChannels.Contains(channelId))
            {
                await ReplyAsync("This channel is already in the list.").ConfigureAwait(false);
                return;
            }

            bool success = await ChannelManager.AddChannelAsync(channelId);
            if (success)
            {
                await ReplyAsync($"Channel {Context.Channel.Name} added to the list.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync("Failed to save the channel list. Please try again later.").ConfigureAwait(false);
            }
        }

        [Command("removechannel")]
        [Summary("Removes the current channel from the list of channels for status updates.")]
        [RequireSudo]
        public async Task RemoveChannelAsync()
        {
            if (BanManager.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var channelId = Context.Channel.Id;
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            if (!availableChannels.Contains(channelId))
            {
                await ReplyAsync("This channel is not in the list.").ConfigureAwait(false);
                return;
            }

            bool success = await ChannelManager.RemoveChannelAsync(channelId);
            if (success)
            {
                await ReplyAsync($"Channel {Context.Channel.Name} removed from the list.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync("Failed to save the channel list. Please try again later.").ConfigureAwait(false);
            }
        }
    }
}

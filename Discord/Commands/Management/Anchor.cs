using System;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class Anchor : ModuleBase<SocketCommandContext>
    {
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        [Command("setAnchor")]
        [Summary("Sets one of the anchors required for the queue loop.")]
        [RequireSudo]
        public async Task SetAnchorAsync(int anchorId)
        {
            var bot = Globals.Bot;

            await Task.Delay(2_000, CancellationToken.None).ConfigureAwait(false);
            var success = await bot.UpdateAnchor(anchorId, CancellationToken.None).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithColor(success ? Color.Green : Color.Red)
                .WithTitle(success ? "✅ Anchor Set" : "❌ Anchor Set Failed")
                .WithDescription(success
                    ? $"Successfully updated anchor to **{anchorId}**."
                    : $"Failed to update anchor to **{anchorId}**. Please check if the anchor ID is valid.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }

        [Command("loadAnchor")]
        [Summary("Loads one of the anchors required for the queue loop. Should only be used for testing, ensure you're in the correct scene, otherwise the game may crash.")]
        [RequireSudo]
        public async Task SendAnchorBytesAsync(int anchorId)
        {
            var bot = Globals.Bot;

            await Task.Delay(2_000, CancellationToken.None).ConfigureAwait(false);
            var success = await bot.SendAnchorBytes(anchorId, CancellationToken.None).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithColor(success ? Color.Green : Color.Red)
                .WithTitle(success ? "✅ Anchor Loaded" : "❌ Anchor Load Failed")
                .WithDescription(success
                    ? $"Successfully set player to anchor **{anchorId}**."
                    : $"Failed to set player to anchor **{anchorId}**. Ensure you're in the correct game scene.")
                .WithFooter($"Requested by {Context.User.Username}", Context.User.GetAvatarUrl())
                .WithTimestamp(DateTimeOffset.Now)
                .Build();

            await ReplyAsync(embed: embed).ConfigureAwait(false);
        }
    }
}

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace SysBot.ACNHOrders.Discord.Commands.General
{
    public class InfoModule : ModuleBase<SocketCommandContext>
    {
        private const string DiscordUrl = "https://pokelegends.org";
        private static readonly string[] Contributors = { "Kurt", "Berichan", "CodeHedge", "DeVry" };
        private static readonly ulong[] DisallowedUserIds =
        {
            195756980873199618, 263105481155936257, 807410947827826688, 291107598030340106,
            330796509215850506, 202076667944894464, 476121600446693378, 1058105885844574321,
            1065472784517574666, 778252332285689897, 510877708385910809
        };

        [Command("info")]
        [Alias("about", "whoami", "owner")]
        public async Task InfoAsync()
        {
            if (DisallowedUserIds.Contains(Context.User.Id))
            {
                await ReplyAsync("We don't let shady people use this command.").ConfigureAwait(false);
                return;
            }

            if (GlobalBan.IsServerBanned(Context.Guild.Id.ToString()))
            {
                await Context.Guild.LeaveAsync().ConfigureAwait(false);
                return;
            }

            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
            };

            var contributors = string.Join(", ", Contributors);
            builder.AddField("Info",
                $"- {Format.Bold("Contributions")}: {contributors}\n" +
                $"- [Pokemon Legends]({DiscordUrl})\n" +
                $"- {Format.Bold("Owner")}: {app.Owner} ({app.Owner.Id})\n" +
                $"- {Format.Bold("Library")}: Discord.Net ({DiscordConfig.Version})\n" +
                $"- {Format.Bold("Uptime")}: {GetUptime()}\n" +
                $"- {Format.Bold("Runtime")}: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture} " +
                $"({RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture})\n" +
                $"- {Format.Bold("Buildtime")}: {GetBuildTime()}\n");

            builder.AddField("Stats",
                $"- {Format.Bold("Heap Size")}: {GetHeapSize()} MiB\n" +
                $"- {Format.Bold("Guilds")}: {Context.Client.Guilds.Count}\n" +
                $"- {Format.Bold("Channels")}: {Context.Client.Guilds.Sum(g => g.Channels.Count)}\n" +
                $"- {Format.Bold("Users")}: {Context.Client.Guilds.Sum(g => g.Users.Count)}\n");

            await ReplyAsync("Here's a bit about me!", embed: builder.Build()).ConfigureAwait(false);
        }

        private static string GetUptime() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");

        private static string GetHeapSize() => (GC.GetTotalMemory(true) / (1024.0 * 1024.0)).ToString("F2", CultureInfo.CurrentCulture);

        private static string GetBuildTime()
        {
            var filePath = Path.Combine(AppContext.BaseDirectory, AppDomain.CurrentDomain.FriendlyName);
            return File.Exists(filePath)
                ? File.GetLastWriteTime(filePath).ToString("yy-MM-dd.HH:mm", CultureInfo.CurrentCulture)
                : DateTime.Now.ToString("yy-MM-dd.HH:mm", CultureInfo.CurrentCulture);
        }
    }
}

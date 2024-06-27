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

namespace SysBot.ACNHOrders
{
    // src: https://github.com/foxbot/patek/blob/master/src/Patek/Modules/InfoModule.cs
    // ISC License (ISC)
    // Copyright 2017, Christopher F. <foxbot@protonmail.com>
    // ReSharper disable once UnusedType.Global
    public class InfoModule : ModuleBase<SocketCommandContext>
    {
        [Command("info")]
        [Alias("about", "whoami", "owner")]
        public async Task InfoAsync()
        {
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

            builder.AddField("Info", $"- {Format.Bold("Owner")}: {app.Owner} ({app.Owner.Id})\n" +
                                    $"- {Format.Bold("Library")}: Discord.Net ({DiscordConfig.Version})\n" +
                                    $"- {Format.Bold("Uptime")}: {GetUptime()}\n" +
                                    $"- {Format.Bold("Runtime")}: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture} " +
                                    $"({RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture})\n" +
                                    $"- {Format.Bold("Buildtime")}: {GetBuildTime()}\n");

            builder.AddField("Stats", $"- {Format.Bold("Heap Size")}: {GetHeapSize()} MiB\n" +
                                      $"- {Format.Bold("Guilds")}: {Context.Client.Guilds.Count}\n" +
                                      $"- {Format.Bold("Channels")}: {Context.Client.Guilds.Sum(g => g.Channels.Count)}\n" +
                                      $"- {Format.Bold("Users")}: {Context.Client.Guilds.Sum(g => g.Users.Count)}\n");

            await ReplyAsync("Here's a bit about me!", embed: builder.Build()).ConfigureAwait(false);
        }

        private static string GetUptime() => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"dd\.hh\:mm\:ss");

        private static string GetHeapSize() => Math.Round(GC.GetTotalMemory(true) / (1024.0 * 1024.0), 2).ToString(CultureInfo.CurrentCulture);

        private static string GetBuildTime()
        {
            var baseDirectory = AppContext.BaseDirectory;
            var filePath = Path.Combine(baseDirectory, AppDomain.CurrentDomain.FriendlyName);

            if (File.Exists(filePath))
            {
                return File.GetLastWriteTime(filePath).ToString(@"yy-MM-dd\.hh\:mm");
            }

            return DateTime.Now.ToString(@"yy-MM-dd\.hh\:mm");
        }
    }
}

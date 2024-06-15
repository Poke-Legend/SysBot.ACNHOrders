using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;
using SysBot.ACNHOrders.Twitch;
using SysBot.ACNHOrders.Signalr;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel, TwitchConfig? tConfig = null)
        {
            // Set up logging for Console Window
            LogUtil.Forwarders.Add(Logger);
            static void Logger(string msg, string identity) => Console.WriteLine(GetMessage(msg, identity));
            static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

            var bot = new CrossBot(config);
            var sys = new SysCord(bot);

            Globals.Self = sys;
            Globals.Bot = bot;
            Globals.Hub = QueueHub.CurrentInstance;
            GlobalBan.UpdateConfiguration(config);

            bot.Log("Starting Discord.");
            _ = Task.Run(() => sys.MainAsync(config.Token, cancel), cancel);

            if (tConfig != null && !string.IsNullOrWhiteSpace(tConfig.Token))
            {
                bot.Log("Starting Twitch.");
                _ = new TwitchCrossBot(tConfig, bot);
            }

            if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
            {
                bot.Log("Starting Web.");
                _ = new SignalrCrossBot(config.SignalrConfig, bot);
            }

            if (config.SkipConsoleBotCreation)
            {
                await Task.Delay(Timeout.Infinite, cancel).ConfigureAwait(false);
                return;
            }

            while (!cancel.IsCancellationRequested)
            {
                bot.Log("Starting bot loop.");

                try
                {
                    await bot.RunAsync(cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bot.Log("Bot has terminated due to an error:");
                    bot.Log(ex.Message);
                    if (ex.StackTrace != null)
                    {
                        bot.Log(ex.StackTrace);
                    }
                }

                if (config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
                {
                    await Task.Delay(10_000, cancel).ConfigureAwait(false);
                    bot.Log("Bot is attempting a restart...");
                }
                else
                {
                    bot.Log("Bot has terminated.");
                    break;
                }
            }
        }
    }
}

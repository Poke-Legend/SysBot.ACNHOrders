using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;
using SysBot.ACNHOrders.Signalr;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        private static void Logger(string msg, string identity) => Console.WriteLine(GetMessage(msg, identity));
        private static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel)
        {
            // Set up logging for Console Window
            LogUtil.Forwarders.Add(Logger);

            var bot = new CrossBot(config);
            var sys = new SysCord(bot);

            Globals.Self = sys;
            Globals.Bot = bot;
            Globals.Hub = QueueHub.CurrentInstance;

            bot.Log("Starting Discord.");

            // Start Discord bot task
            var discordTask = sys.MainAsync(config.Token, cancel);

            // Start SignalR bot if configured
            if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
            {
                bot.Log("Starting Web.");
                _ = new SignalrCrossBot(config.SignalrConfig, bot);
            }

            // Skip bot creation if configured
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
                    // Run the bot's main loop
                    await bot.RunAsync(cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    bot.Log("Bot encountered an error:");
                    bot.Log(ex.Message);
                    if (ex.StackTrace != null)
                    {
                        bot.Log(ex.StackTrace);
                    }
                }

                // Always restart after an error
                bot.Log("Bot is restarting...");
                await Task.Delay(10_000, cancel).ConfigureAwait(false);
            }

            // Await the Discord task to ensure proper shutdown handling
            await discordTask.ConfigureAwait(false);
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.ACNHOrders.Signalr;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        private static void Logger(string msg, string identity)
            => Console.WriteLine(GetMessage(msg, identity));

        private static string GetMessage(string msg, string identity)
            => $"> [{DateTime.Now:HH:mm:ss}] - {identity}: {msg}";

        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel)
        {
            // Log unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                Console.WriteLine($"[FATAL] Unhandled Exception: {e.ExceptionObject}");
            };
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                Console.WriteLine($"[FATAL] Unobserved Task Exception: {e.Exception}");
                e.SetObserved();
            };

            // Register a logger with LogUtil
            LogUtil.Forwarders.Add(Logger);

            while (true)
            {
                // If cancellation was triggered from outside, break the loop
                if (cancel.IsCancellationRequested)
                    break;

                try
                {
                    // Create bot instance
                    var bot = new CrossBot(config);
                    // Create SysCord (Discord client wrapper)
                    var sys = new SysCord(bot);

                    // Update global references (if your code needs them)
                    Globals.Self = sys;
                    Globals.Bot = bot;
                    Globals.Hub = QueueHub.CurrentInstance;

                    bot.Log("Starting Discord...");
                    var discordTask = sys.MainAsync(config.Token, cancel);

                    // Start SignalR if configured
                    if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
                    {
                        bot.Log("Starting SignalR client...");
                        _ = new SignalrCrossBot(config.SignalrConfig, bot);
                    }

                    // If we skip console creation, we’d potentially block forever
                    // Decide whether to remove this entirely if you always want the loop
                    // to handle restarts automatically.
                    if (config.SkipConsoleBotCreation)
                    {
                        bot.Log("SkipConsoleBotCreation = true. Waiting indefinitely...");
                        await Task.Delay(Timeout.Infinite, cancel).ConfigureAwait(false);
                        // If you do 'return;' or break here, you exit the entire method
                        // which means no restart. So remove or modify as needed.
                    }

                    // The main run loop
                    while (!cancel.IsCancellationRequested)
                    {
                        bot.Log("Starting bot loop...");
                        await bot.RunAsync(cancel).ConfigureAwait(false);

                        // If you want the bot to repeat 'RunAsync' continuously,
                        // you can keep this while loop. If 'RunAsync' is itself
                        // an infinite loop until canceled, you might not need it.
                    }

                    // If we’re here, it likely means cancellation was triggered
                    // Wait for Discord to finish
                    await discordTask.ConfigureAwait(false);

                    // Break out of the while(true) so we can exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CRITICAL] Bot crashed: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    Console.WriteLine("Restarting in 10 seconds...");

                    // Wait 10 seconds, then re-loop (unless canceled)
                    await Task.Delay(TimeSpan.FromSeconds(10), cancel).ConfigureAwait(false);
                }
            }

            Console.WriteLine("BotRunner: Exiting main loop (canceled or forced).");
        }
    }
}

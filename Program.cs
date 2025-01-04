using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    internal static class Program
    {
        private const string DefaultConfigPath = "config.json";
        private const string DefaultSocketServerAPIPath = "server.json";

        private static async Task Main(string[] args)
        {
            Console.WriteLine("Starting up...");

            // We create a token source so we can support Ctrl+C graceful shutdown
            using var cts = new CancellationTokenSource();

            // Hook Ctrl+C to cancel
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;   // Prevent the process from terminating immediately
                cts.Cancel();      // Signal the token
            };

            // Load or create configs
            string configPath = args.Length == 1 ? args[0] : DefaultConfigPath;
            var config = await LoadOrCreateConfigAsync<CrossBotConfig>(configPath, CreateDefaultConfig);
            var serverConfig = await LoadOrCreateConfigAsync<SocketAPI.SocketAPIServerConfig>(DefaultSocketServerAPIPath,
                () => new SocketAPI.SocketAPIServerConfig());

            // If either config failed to deserialize, bail out
            if (config == null || serverConfig == null)
            {
                Console.WriteLine("Configuration deserialization failed. Exiting.");
                WaitKeyExit();
                return;
            }

            // Start Socket API Server in the background
            var server = SocketAPI.SocketAPIServer.Instance;
            _ = server.Start(serverConfig);

            // IMPORTANT:
            // BotRunner.RunFrom has its own internal `while (true)` loop that catches exceptions
            // and restarts the bot after a delay. It will only return when a CancellationToken
            // is triggered, or if you have logic that explicitly exits.

            await BotRunner.RunFrom(config, cts.Token).ConfigureAwait(false);

            // We only reach here if BotRunner.RunFrom returns normally or the token is canceled
            WaitKeyExit();
        }

        /// <summary>
        /// Attempts to load a JSON config from file.
        /// If it doesn't exist, creates a default one, writes it to disk, and returns null (so you can exit).
        /// </summary>
        private static async Task<T?> LoadOrCreateConfigAsync<T>(string path, Func<T> createDefault) where T : class
        {
            if (!File.Exists(path))
            {
                var defaultConfig = createDefault();
                await SaveConfigAsync(defaultConfig, path);
                Console.WriteLine($"Created default configuration at {path}. Please configure it and restart the program.");
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading configuration from {path}: {ex.Message}");
                return null;
            }
        }

        private static async Task SaveConfigAsync<T>(T config, string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        private static CrossBotConfig CreateDefaultConfig()
        {
            return new CrossBotConfig
            {
                IP = "127.0.0.1",
                Port = 6000,
                // You can add other default properties here, e.g.,
                // SkipConsoleBotCreation = false,
                // etc.
            };
        }

        private static void WaitKeyExit()
        {
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}

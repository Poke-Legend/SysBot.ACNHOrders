using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;

namespace SysBot.ACNHOrders
{
    internal static class Program
    {
        private const string DefaultConfigPath = "config.json";
        private const string DefaultSocketServerAPIPath = "server.json";

        private static async Task Main(string[] args)
        {
            Console.WriteLine("Starting up...");

            string configPath = args.Length == 1 ? args[0] : DefaultConfigPath;

            // Load or create configurations asynchronously
            var config = await LoadOrCreateConfigAsync<CrossBotConfig>(configPath, CreateDefaultConfig);
            var serverConfig = await LoadOrCreateConfigAsync<SocketAPI.SocketAPIServerConfig>(DefaultSocketServerAPIPath, () => new SocketAPI.SocketAPIServerConfig());

            if (config == null || serverConfig == null)
            {
                Console.WriteLine("Configuration deserialization failed. Exiting.");
                WaitKeyExit();
                return;
            }

            // Start Socket API Server
            var server = SocketAPI.SocketAPIServer.Instance;
            _ = server.Start(serverConfig);

            // Run the bot
            await BotRunner.RunFrom(config, CancellationToken.None).ConfigureAwait(false);

            WaitKeyExit();
        }

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
            return new CrossBotConfig { IP = "192.168.0.1", Port = 6000 };
        }

        private static void WaitKeyExit()
        {
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}
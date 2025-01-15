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

            // Determine config path from args
            string configPath = args.Length switch
            {
                > 1 => WarnAndReturnDefaultConfigPath(),
                1 => args[0],
                _ => DefaultConfigPath
            };

            // If the main config doesn't exist, create a default one and quit
            if (!File.Exists(configPath))
            {
                CreateConfigQuit(configPath);
                return;
            }

            // Ensure the socket server config file exists (create default if missing)
            EnsureFileExists(DefaultSocketServerAPIPath, () => new SocketAPI.SocketAPIServerConfig());

            // Load configurations
            var config = LoadConfig<CrossBotConfig>(configPath);
            var serverConfig = LoadConfig<SocketAPI.SocketAPIServerConfig>(DefaultSocketServerAPIPath);

            // If any config failed to deserialize, exit
            if (config == null || serverConfig == null)
            {
                WaitKeyExit();
                return;
            }

            // Save them back (in case of formatting or first-time creation)
            SaveConfig(config, configPath);
            SaveConfig(serverConfig, DefaultSocketServerAPIPath);

            // Start the socket server
            var server = SocketAPI.SocketAPIServer.Instance;
            _ = server.Start(serverConfig);

            // Run the bot
            await BotRunner
                .RunFrom(config, CancellationToken.None)
                .ConfigureAwait(false);

            WaitKeyExit();
        }

        /// <summary>
        /// Logs a warning about too many arguments and returns the default config path.
        /// </summary>
        private static string WarnAndReturnDefaultConfigPath()
        {
            Console.WriteLine("Too many arguments supplied; they will be ignored.");
            return DefaultConfigPath;
        }

        /// <summary>
        /// Ensures the specified config file exists, creating a default version if missing.
        /// </summary>
        private static void EnsureFileExists<T>(string path, Func<T> defaultConfigFactory)
        {
            if (!File.Exists(path))
            {
                SaveConfig(defaultConfigFactory(), path);
            }
        }

        /// <summary>
        /// Attempts to load and deserialize a config object from the specified path.
        /// Returns null (with a console message) on error.
        /// </summary>
        private static T? LoadConfig<T>(string path) where T : class
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to deserialize configuration from '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Serializes a config object to JSON and writes it to the specified path.
        /// </summary>
        private static void SaveConfig<T>(T config, string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(path, json);
        }

        /// <summary>
        /// Creates a blank configuration file and instructs the user to configure it before exiting.
        /// </summary>
        private static void CreateConfigQuit(string configPath)
        {
            SaveConfig(new CrossBotConfig { IP = "192.168.0.1", Port = 6000 }, configPath);
            Console.WriteLine("Created a blank config file. Please configure it and restart the program.");
            WaitKeyExit();
        }

        /// <summary>
        /// Prompts user to press any key, then exits.
        /// </summary>
        private static void WaitKeyExit()
        {
            Console.WriteLine("Press any key to exit.");
            Console.ReadKey();
        }
    }
}

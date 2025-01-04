using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using SysBot.ACNHOrders.Discord.Helpers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public sealed class SysCord
    {
        private readonly DiscordSocketClient _client;
        private readonly CrossBot _bot;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;
        private bool _isOffline;

        public ulong Owner { get; private set; } = ulong.MaxValue;
        public bool Ready { get; private set; }

        public SysCord(CrossBot bot)
        {
            _bot = bot;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds |
                                 GatewayIntents.GuildMessages |
                                 GatewayIntents.DirectMessages |
                                 GatewayIntents.GuildMembers |
                                 GatewayIntents.MessageContent
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                DefaultRunMode = RunMode.Sync,
                CaseSensitiveCommands = false
            });

            // Setup logs
            _client.Log += LogAsync;
            _commands.Log += LogAsync;

            // Build our DI container
            _services = new ServiceCollection()
                .AddSingleton(bot)
                .BuildServiceProvider();

            _isOffline = false;

            // CHANGED: Remove forced process exit handlers.
            // AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            // Console.CancelKeyPress += OnConsoleCancelKeyPress;
        }

        // CHANGED: Remove OnProcessExit & OnConsoleCancelKeyPress
        // If you keep them, do NOT call Environment.Exit(0) inside them.

        // Main Initialization
        public async Task MainAsync(string apiToken, CancellationToken token)
        {
            await InitCommandsAsync();
            await _client.LoginAsync(TokenType.Bot, apiToken);
            await _client.StartAsync();

            _client.Ready += OnClientReadyAsync;

            if (!string.IsNullOrWhiteSpace(_bot.Config.Name))
                await _client.SetGameAsync(_bot.Config.Name);

            var appInfo = await _client.GetApplicationInfoAsync();
            Owner = appInfo.Owner.Id;

            // Wait until canceled, monitoring status in the background
            await MonitorStatusAsync(token);
        }

        private async Task InitCommandsAsync()
        {
            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
            _client.MessageReceived += HandleMessageAsync;
        }

        // Command Handling
        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int argPos)
        {
            var context = new SocketCommandContext(_client, msg);

            // Example: fetch permission data from GitHub
            var sudoJson = await GitHubApi.FetchFileContentAsync(GitHubApi.SudoApiUrl);
            if (sudoJson == null)
            {
                var errorEmbed = new EmbedBuilder()
                    .WithTitle("Permission Error")
                    .WithDescription("Failed to fetch permissions from GitHub. Please try again later.")
                    .WithColor(Color.DarkRed)
                    .WithCurrentTimestamp()
                    .Build();

                await msg.Channel.SendMessageAsync(embed: errorEmbed);
                return false;
            }

            // Check if the user or server is banned
            if (await IsUserBannedAsync(context) || await IsServerBannedAsync(context))
                return true;

            // Sudo users
            var sudoUserIds = JsonConvert.DeserializeObject<List<ulong>>(sudoJson) ?? new List<ulong>();

            // Allowed channels
            var channelJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            var allowedChannelIds = channelJson != null
                ? JsonConvert.DeserializeObject<List<ulong>>(channelJson) ?? new List<ulong>()
                : new List<ulong>();

            // Evaluate permissions
            if (sudoUserIds.Contains(msg.Author.Id) ||
                allowedChannelIds.Contains(msg.Channel.Id) ||
                msg.Author.Id == Owner)
            {
                var result = await _commands.ExecuteAsync(context, argPos, _services);

                if (!result.IsSuccess)
                {
                    var errorEmbed = new EmbedBuilder()
                        .WithTitle("❌ Command Error")
                        .WithDescription(result.ErrorReason)
                        .WithColor(Color.Orange)
                        .WithFooter("Check command syntax/permissions.")
                        .WithCurrentTimestamp()
                        .Build();

                    await msg.Channel.SendMessageAsync(embed: errorEmbed);
                }

                // Return true if we handled a known command
                return result.Error != CommandError.UnknownCommand;
            }
            else
            {
                var denyEmbed = new EmbedBuilder()
                    .WithTitle("Access Denied")
                    .WithDescription("You don't have permission to use this command here.")
                    .WithColor(Color.Orange)
                    .WithFooter("Contact an admin if this is an error.")
                    .WithCurrentTimestamp()
                    .Build();

                await msg.Channel.SendMessageAsync(embed: denyEmbed);
                return true;
            }
        }

        private static async Task<bool> IsUserBannedAsync(SocketCommandContext context)
        {
            if (BanManager.IsUserBanned(context.User.Id.ToString()))
            {
                var bannedEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Banned")
                    .WithDescription($"{context.User.Mention}, you have been banned.")
                    .Build();
                await context.Channel.SendMessageAsync(embed: bannedEmbed).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private static async Task<bool> IsServerBannedAsync(SocketCommandContext context)
        {
            if (context.Guild != null && BanManager.IsServerBanned(context.Guild.Id.ToString()))
            {
                var embed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("🚫 Server Banned")
                    .WithDescription("This server has been blacklisted. The bot will now leave.")
                    .AddField("Appeal", "Please contact support if this is an error.")
                    .Build();

                await context.Channel.SendMessageAsync(embed: embed).ConfigureAwait(false);
                await context.Guild.LeaveAsync().ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            if (arg is not SocketUserMessage msg ||
                msg.Author.Id == _client.CurrentUser.Id ||
                (!_bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            int argPos = 0;

            // If message has prefix, it might be a command
            if (msg.HasStringPrefix(_bot.Config.Prefix, ref argPos))
            {
                if (await TryHandleCommandAsync(msg, argPos))
                    return;
            }
            else if (await CheckMessageDeletionAsync(msg))
            {
                // We might have deleted a non-command message
                return;
            }

            // Otherwise, do whatever "regular message" handling you want
            await TryHandleMessage(msg);
        }

        private static Task TryHandleMessage(SocketMessage msg)
        {
            // If you have additional logic for normal messages, handle it here
            return Task.CompletedTask;
        }

        /// <summary>
        /// Attempts to post a message to the specified channel ID. 
        /// Optionally checks for duplicate (double) posts if requested.
        /// Returns true if successful, false otherwise.
        /// </summary>
        public async Task<bool> TrySpeakMessage(ulong channelId, string message, bool checkForDoublePosts = false)
        {
            try
            {
                // Attempt to retrieve the channel
                if (_client.GetChannel(channelId) is IMessageChannel textChannel)
                {
                    if (checkForDoublePosts)
                    {
                        // Fetch the last message
                        var lastMessage = (await textChannel.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                        if (lastMessage != null && lastMessage.Content == message)
                        {
                            // Already posted an identical message, so return true (no new post needed)
                            return true;
                        }
                    }

                    // Send the message
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    // The channel ID was invalid or not a text channel
                    Console.WriteLine($"SysCord: channel ID {channelId} is not a valid text channel.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                // Log or handle exceptions as needed
                Console.WriteLine($"SysCord: TrySpeakMessage failed - {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CheckMessageDeletionAsync(SocketUserMessage msg)
        {
            var context = new SocketCommandContext(_client, msg);

            if (!Globals.Bot.Config.DeleteNonCommands ||
                context.IsPrivate ||
                msg.Author.IsBot ||
                Globals.Bot.Config.CanUseSudo(msg.Author.Id) ||
                msg.Author.Id == Owner)
            {
                return false;
            }

            if (!Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            try
            {
                await msg.DeleteAsync();
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} - Only bot commands are allowed here.\nDeleted Message:```\n{msg.Content}\n```");
            }
            catch (HttpException ex)
            {
                Console.WriteLine($"Failed to delete message in {context.Channel.Name} due to: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            return true;
        }

        private static Task LogAsync(LogMessage msg)
        {
            Console.ForegroundColor = msg.Severity switch
            {
                LogSeverity.Critical or LogSeverity.Error => ConsoleColor.Red,
                LogSeverity.Warning => ConsoleColor.Yellow,
                LogSeverity.Info => ConsoleColor.White,
                LogSeverity.Verbose or LogSeverity.Debug => ConsoleColor.DarkGray,
                _ => Console.ForegroundColor
            };

            string logText = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
            Console.WriteLine($"{DateTime.Now,-19} {logText}");
            Console.ResetColor();

            LogUtil.LogText($"SysCord: {logText}");
            return Task.CompletedTask;
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int intervalSeconds = 20;
            UserStatus currentState = UserStatus.Idle;

            while (!token.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(intervalSeconds);
                var newStatus = _bot.Config.AcceptingCommands ? UserStatus.Online : UserStatus.DoNotDisturb;

                if (newStatus != currentState)
                {
                    currentState = newStatus;
                    await _client.SetStatusAsync(currentState);
                }

                if (_bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode &&
                    _bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                {
                    await _client.SetGameAsync($"Dodo code: {_bot.DodoCode}");
                }

                await Task.Delay(delay, token);
            }
        }

        private async Task OnClientReadyAsync()
        {
            if (Ready) return;
            Ready = true;

            // Example: Forward logs to Discord channels
            foreach (var channelId in _bot.Config.LoggingChannels)
            {
                if (_client.GetChannel(channelId) is not ISocketMessageChannel channel)
                {
                    Console.WriteLine($"{channelId} is null or couldn't be found.");
                    continue;
                }

                void Logger(string msg, string identity)
                    => channel.SendMessageAsync($"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}");
                LogUtil.Forwarders.Add(Logger);
            }

            await UpdateChannelNameWithStatus("🟢");
        }

        public async Task UpdateChannelNameWithStatus(string statusIcon)
        {
            var availableChannels = await ChannelManager.LoadChannelsAsync();

            foreach (var channelId in availableChannels)
            {
                if (_client.GetChannel(channelId) is not SocketTextChannel channel)
                    continue;

                try
                {
                    string currentName = channel.Name;

                    // If the name starts with a status icon, remove it
                    if (currentName.StartsWith("🟢") || currentName.StartsWith("🔴"))
                        currentName = currentName.Substring(1).Trim();

                    // Prepend the new status
                    string newChannelName = string.IsNullOrEmpty(statusIcon)
                        ? currentName
                        : $"{statusIcon}{currentName}";

                    await channel.ModifyAsync(prop => prop.Name = newChannelName);
                    Console.WriteLine($"Channel name updated to: {newChannelName} in {channel.Guild.Name}");
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (ex.Data["Retry-After"] is int retryAfterMs)
                    {
                        Console.WriteLine($"Rate limit hit. Retrying after {retryAfterMs} ms...");
                        await Task.Delay(retryAfterMs);

                        // Retry
                        string retryChannelName = string.IsNullOrEmpty(statusIcon)
                            ? channel.Name.Substring(1).Trim()
                            : $"{statusIcon}{channel.Name.Substring(1).Trim()}";
                        await channel.ModifyAsync(prop => prop.Name = retryChannelName);
                        Console.WriteLine($"Channel name updated to: {retryChannelName} after retry");
                    }
                    else
                    {
                        Console.WriteLine($"Rate limit hit, no Retry-After header: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to update channel name in {channel.Name}: {ex.Message}");
                }
            }
        }

        // Shutdown and Restart
        private async Task ShutdownAsync()
        {
            // CHANGED: Removed Environment.Exit(0)
            SetOfflineMode(true);
            await UpdateChannelNameWithStatus("🔴");
            await _client.StopAsync();

            // Let the main program handle whether to exit or restart
        }

        public async Task StopBotAsync()
        {
            SetOfflineMode(true);
            await UpdateChannelNameWithStatus("🔴");
            await _client.StopAsync();
        }

        public async Task RestartBotAsync(string apiToken)
        {
            SetOfflineMode(false);
            await _client.LoginAsync(TokenType.Bot, apiToken);
            await _client.StartAsync();
            await UpdateChannelNameWithStatus("🟢");
        }

        public void SetOfflineMode(bool isOffline)
        {
            _isOffline = isOffline;
        }
    }
}

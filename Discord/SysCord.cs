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

            _client.Log += LogAsync;
            _commands.Log += LogAsync;

            _services = new ServiceCollection()
                .AddSingleton(bot)
                .BuildServiceProvider();

            _isOffline = false;

            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnConsoleCancelKeyPress;
        }

        // Lifecycle Event Handlers
        private void OnProcessExit(object? sender, EventArgs e)
        {
            ShutdownAsync().Wait();
        }

        private void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            ShutdownAsync().Wait();
        }

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

            // Fetch the list of sudo users from GitHub
            var sudoJson = await GitHubApi.FetchFileContentAsync(GitHubApi.SudoApiUrl);
            if (sudoJson == null)
            {
                await msg.Channel.SendMessageAsync("Failed to fetch permissions from GitHub.");
                return true;
            }

            // Deserialize sudo user IDs from JSON
            var sudoUserIds = JsonConvert.DeserializeObject<List<ulong>>(sudoJson) ?? new List<ulong>();

            // Optionally fetch channel permissions if needed
            var channelJson = await GitHubApi.FetchFileContentAsync(GitHubApi.ChannelListApiUrl);
            var allowedChannelIds = channelJson != null
                ? JsonConvert.DeserializeObject<List<ulong>>(channelJson) ?? new List<ulong>()
                : new List<ulong>();

            // Check if the user is permitted
            if (!sudoUserIds.Contains(msg.Author.Id) && msg.Author.Id != Owner)
            {
                await msg.Channel.SendMessageAsync("You are not permitted to use this command.");
                return true;
            }

            // Check if the command is allowed in the current channel
            if (!allowedChannelIds.Contains(msg.Channel.Id) && msg.Author.Id != Owner && !sudoUserIds.Contains(msg.Author.Id))
            {
                await msg.Channel.SendMessageAsync("You can't use that command here.");
                return true;
            }

            // Execute the command
            var result = await _commands.ExecuteAsync(context, argPos, _services);

            if (!result.IsSuccess)
                await msg.Channel.SendMessageAsync(result.ErrorReason);

            return result.Error != CommandError.UnknownCommand;
        }


        private async Task HandleMessageAsync(SocketMessage arg)
        {
            // Check if the message is from a user and not the bot itself
            if (arg is not SocketUserMessage msg ||
                msg.Author.Id == _client.CurrentUser.Id ||
                (!_bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            int argPos = 0;

            // Check if the message is in a guild channel and if the server is blacklisted
            if (msg.Channel is SocketGuildChannel guildChannel)
            {
                if (await AllowedManager.ServerBlacklisted(guildChannel.Guild.Id))
                {
                    await guildChannel.Guild.LeaveAsync();
                    return;
                }
            }

            // Attempt to handle commands with the specified prefix
            if (msg.HasStringPrefix(_bot.Config.Prefix, ref argPos))
            {
                if (await TryHandleCommandAsync(msg, argPos))
                    return;
            }
            // Check if message should be deleted
            else if (await CheckMessageDeletionAsync(msg))
            {
                return;
            }

            // If none of the above conditions match, try handling it as a regular message
            await TryHandleMessage(msg);
        }

        private static Task TryHandleMessage(SocketMessage msg)
        {
            ArgumentNullException.ThrowIfNull(msg);
            return Task.CompletedTask;
        }

        private async Task<bool> CheckMessageDeletionAsync(SocketUserMessage msg)
        {
            var context = new SocketCommandContext(_client, msg);

            if (!Globals.Bot.Config.DeleteNonCommands || context.IsPrivate || msg.Author.IsBot || Globals.Bot.Config.CanUseSudo(msg.Author.Id) || msg.Author.Id == Owner)
                return false;

            if (!Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            try
            {
                await msg.DeleteAsync(RequestOptions.Default);
                await msg.Channel.SendMessageAsync($"{msg.Author.Mention} - The order channels are for bot commands only.\nDeleted Message:```\n{msg.Content}\n```");
            }
            catch (HttpException ex)
            {
                Console.WriteLine($"Failed to delete message in {context.Channel.Name} (ID: {context.Channel.Id}) due to: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            return true;
        }

        // Status Monitoring and Logging
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

                if (_bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && _bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                {
                    await _client.SetGameAsync($"Dodo code: {_bot.DodoCode}");
                }

                await Task.Delay(delay, token);
            }
        }

        // Client Ready Event Handling
        private async Task OnClientReadyAsync()
        {
            if (Ready) return;
            Ready = true;

            foreach (var channelId in _bot.Config.LoggingChannels)
            {
                if (_client.GetChannel(channelId) is not ISocketMessageChannel channel)
                {
                    Console.WriteLine($"{channelId} is null or couldn't be found.");
                    continue;
                }

                void Logger(string msg, string identity) => channel.SendMessageAsync($"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}");
                LogUtil.Forwarders.Add(Logger);
            }

            await UpdateChannelNameWithStatus("🟢");
        }

        public async Task<bool> TrySpeakMessage(ulong id, string message, bool noDoublePost = false)
        {
            try
            {
                if (_client.ConnectionState != ConnectionState.Connected)
                    return false;
                var channel = _client.GetChannel(id);
                if (noDoublePost && channel is IMessageChannel msgChannel)
                {
                    var lastMsg = await msgChannel.GetMessagesAsync(1).FlattenAsync();
                    if (lastMsg != null && lastMsg.Any())
                        if (lastMsg.ElementAt(0).Content == message)
                            return true; // exists
                }

                if (channel is IMessageChannel textChannel)
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                if (e.StackTrace != null)
                    LogUtil.LogError($"SpeakMessage failed with:\n{e.Message}\n{e.StackTrace}", nameof(SysCord));
                else
                    LogUtil.LogError($"SpeakMessage failed with:\n{e.Message}", nameof(SysCord));
            }

            return false;
        }

        public static async Task<bool> TrySpeakMessage(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch { }

            return false;
        }

        public static async Task<bool> TrySendMessage(IMessageChannel channel, string message, bool noDoublePost = false)
        {
            try
            {
                if (noDoublePost)
                {
                    var lastMessage = (await channel.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                    if (lastMessage?.Content == message) return true;
                }

                await channel.SendMessageAsync(message);
                return true;
            }
            catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                if (ex.Data.Contains("Retry-After"))
                {
                    var retryAfterMs = Convert.ToInt32(ex.Data["Retry-After"]);
                    Console.WriteLine($"Rate limit hit. Retrying after {retryAfterMs} milliseconds...");
                    await Task.Delay(retryAfterMs);
                    await channel.SendMessageAsync(message);
                    return true;
                }
                Console.WriteLine($"Rate limit hit, but no Retry-After header found: {ex.Message}");
            }
            catch (HttpException ex)
            {
                Console.WriteLine($"Failed to send message in {channel.Name}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            return false;
        }

        // Inside SysCord.cs - UpdateChannelNameWithStatus method

        public async Task UpdateChannelNameWithStatus(string statusIcon)
        {
            var availableChannels = await ChannelManager.LoadChannelsAsync(); // Static async call to load channels

            foreach (var channelId in availableChannels)
            {
                if (_client.GetChannel(channelId) is not SocketTextChannel channel)
                    continue;

                try
                {
                    string currentName = channel.Name;

                    // Remove any existing status icon (assuming icons are either "🟢" or "🔴" at the start)
                    if (currentName.StartsWith("🟢") || currentName.StartsWith("🔴"))
                        currentName = currentName.Substring(1).Trim(); // Remove existing status icon if present

                    // Update the channel name with the status icon if provided
                    string newChannelName = string.IsNullOrEmpty(statusIcon) ? currentName : $"{statusIcon}{currentName}";
                    await channel.ModifyAsync(prop => prop.Name = newChannelName);
                    Console.WriteLine($"Channel name updated to: {newChannelName} in {channel.Guild.Name}");
                }
                catch (HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (ex.Data["Retry-After"] is int retryAfterMs)
                    {
                        Console.WriteLine($"Rate limit hit. Retrying after {retryAfterMs} milliseconds...");
                        await Task.Delay(retryAfterMs);

                        // Retry the modification after delay
                        string retryChannelName = string.IsNullOrEmpty(statusIcon) ? channel.Name.Substring(1).Trim() : $"{statusIcon}{channel.Name.Substring(1).Trim()}";
                        await channel.ModifyAsync(prop => prop.Name = retryChannelName);
                        Console.WriteLine($"Channel name updated to: {retryChannelName} in {channel.Guild.Name} after retry");
                    }
                    else
                    {
                        Console.WriteLine($"Rate limit hit, but no Retry-After header found: {ex.Message}");
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
            SetOfflineMode(true);
            await UpdateChannelNameWithStatus("🔴");
            await _client.StopAsync();
            Environment.Exit(0);
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

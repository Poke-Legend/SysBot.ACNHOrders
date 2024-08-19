using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SysBot.Base;
using Discord.Net;

namespace SysBot.ACNHOrders
{
    public sealed class SysCord
    {
        private readonly DiscordSocketClient _client;
        private readonly CrossBot _bot;
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public ulong Owner { get; private set; } = ulong.MaxValue;
        public bool Ready { get; private set; } = false;

        public SysCord(CrossBot bot)
        {
            _bot = bot;

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.GuildMembers | GatewayIntents.MessageContent
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

            var logText = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
            Console.WriteLine($"{DateTime.Now,-19} {logText}");
            Console.ResetColor();

            LogUtil.LogText($"SysCord: {logText}");

            return Task.CompletedTask;
        }

        public async Task MainAsync(string apiToken, CancellationToken token)
        {
            await InitCommandsAsync();

            await _client.LoginAsync(TokenType.Bot, apiToken);
            await _client.StartAsync();
            _client.Ready += OnClientReadyAsync;

            if (!string.IsNullOrWhiteSpace(_bot.Config.Name))
            {
                await _client.SetGameAsync(_bot.Config.Name);
            }

            var appInfo = await _client.GetApplicationInfoAsync();
            Owner = appInfo.Owner.Id;

            await MonitorStatusAsync(token);
        }

        private Task OnClientReadyAsync()
        {
            if (Ready)
                return Task.CompletedTask;
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

            return Task.CompletedTask;
        }

        public async Task InitCommandsAsync()
        {
            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
            _client.MessageReceived += HandleMessageAsync;
        }

        public async Task<bool> TrySpeakMessageAsync(ulong channelId, string message, bool noDoublePost = false)
        {
            if (_client.ConnectionState != ConnectionState.Connected) return false;

            if (_client.GetChannel(channelId) is IMessageChannel channel)
            {
                if (noDoublePost)
                {
                    var lastMessage = (await channel.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                    if (lastMessage?.Content == message) return true;
                }

                try
                {
                    await channel.SendMessageAsync(message);
                    return true;
                }
                catch (HttpException ex)
                {
                    Console.WriteLine($"Failed to send message in {channel.Name} (ID: {channel.Id}) due to: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                }
            }

            return false;
        }

        public static async Task<bool> TrySpeakMessageAsync(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message);
                return true;
            }
            catch (HttpException ex)
            {
                Console.WriteLine($"Failed to send message in {channel.Name} (ID: {channel.Id}) due to: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }

            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            if (arg is not SocketUserMessage msg || msg.Author.Id == _client.CurrentUser.Id || (!_bot.Config.IgnoreAllPermissions && msg.Author.IsBot)) return;

            int argPos = 0;

            if (msg.HasStringPrefix(_bot.Config.Prefix, ref argPos))
            {
                if (await TryHandleCommandAsync(msg, argPos)) return;
            }
            else if (await CheckMessageDeletionAsync(msg)) return;

            await TryHandleMessageAsync(msg);
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

        private static Task TryHandleMessageAsync(SocketMessage msg)
        {
            // Placeholder for handling messages with attachments.
            return Task.CompletedTask;
        }

        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int argPos)
        {
            var context = new SocketCommandContext(_client, msg);

            if (!_bot.Config.IgnoreAllPermissions)
            {
                if (!_bot.Config.CanUseCommandUser(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You are not permitted to use this command.");
                    return true;
                }

                if (!_bot.Config.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != Owner && !_bot.Config.CanUseSudo(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You can't use that command here.");
                    return true;
                }
            }

            var result = await _commands.ExecuteAsync(context, argPos, _services);

            if (!result.IsSuccess)
            {
                await msg.Channel.SendMessageAsync(result.ErrorReason);
            }

            return result.Error != CommandError.UnknownCommand;
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int intervalSeconds = 20;
            UserStatus currentState = UserStatus.Idle;

            while (!token.IsCancellationRequested)
            {
                var timeSinceLastLog = DateTime.Now - LogUtil.LastLogged;
                var delay = TimeSpan.FromSeconds(intervalSeconds) - timeSinceLastLog;

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

                await Task.Delay(delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(intervalSeconds) : delay, token);
            }
        }
    }
}

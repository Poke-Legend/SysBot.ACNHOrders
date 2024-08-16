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

            _services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton<CrossBot>()
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
            await InitCommandsAsync().ConfigureAwait(false);

            await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);
            _client.Ready += OnClientReadyAsync;

            await Task.Delay(5000, token).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(_bot.Config.Name))
            {
                await _client.SetGameAsync(_bot.Config.Name).ConfigureAwait(false);
            }

            var appInfo = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
            Owner = appInfo.Owner.Id;

            await MonitorStatusAsync(token).ConfigureAwait(false);
        }

        private async Task OnClientReadyAsync()
        {
            if (Ready) return;
            Ready = true;

            await Task.Delay(1000).ConfigureAwait(false);

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

            await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task InitCommandsAsync()
        {
            await _commands.AddModulesAsync(Assembly.GetExecutingAssembly(), _services).ConfigureAwait(false);
            _client.MessageReceived += HandleMessageAsync;
        }

        public async Task<bool> TrySpeakMessageAsync(ulong channelId, string message, bool noDoublePost = false)
        {
            try
            {
                if (_client.ConnectionState != ConnectionState.Connected) return false;

                if (_client.GetChannel(channelId) is IMessageChannel channel)
                {
                    if (noDoublePost)
                    {
                        var lastMessage = (await channel.GetMessagesAsync(1).FlattenAsync()).FirstOrDefault();
                        if (lastMessage?.Content == message) return true;
                    }

                    await channel.SendMessageAsync(message).ConfigureAwait(false);
                    return true;
                }
            }
            catch (Exception)
            {
                // Optionally log the exception here.
            }

            return false;
        }

        public static async Task<bool> TrySpeakMessageAsync(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception)
            {
                // Optionally log the exception here.
            }

            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            if (arg is not SocketUserMessage msg || msg.Author.Id == _client.CurrentUser.Id || (!_bot.Config.IgnoreAllPermissions && msg.Author.IsBot)) return;

            int argPos = 0;

            if (msg.HasStringPrefix(_bot.Config.Prefix, ref argPos))
            {
                if (await TryHandleCommandAsync(msg, argPos).ConfigureAwait(false)) return;
            }
            else if (await CheckMessageDeletionAsync(msg).ConfigureAwait(false)) return;

            await TryHandleMessageAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> CheckMessageDeletionAsync(SocketUserMessage msg)
        {
            var context = new SocketCommandContext(_client, msg);

            if (!Globals.Bot.Config.DeleteNonCommands || context.IsPrivate || msg.Author.IsBot || Globals.Bot.Config.CanUseSudo(msg.Author.Id) || msg.Author.Id == Owner)
                return false;

            if (!Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            await msg.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            await msg.Channel.SendMessageAsync($"{msg.Author.Mention} - The order channels are for bot commands only.\nDeleted Message:```\n{msg.Content}\n```").ConfigureAwait(false);

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
                    await msg.Channel.SendMessageAsync("You are not permitted to use this command.").ConfigureAwait(false);
                    return true;
                }

                if (!_bot.Config.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != Owner && !_bot.Config.CanUseSudo(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You can't use that command here.").ConfigureAwait(false);
                    return true;
                }
            }

            var result = await _commands.ExecuteAsync(context, argPos, _services).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                await msg.Channel.SendMessageAsync(result.ErrorReason).ConfigureAwait(false);
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
                    await _client.SetStatusAsync(currentState).ConfigureAwait(false);
                }

                if (_bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && _bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                {
                    await _client.SetGameAsync($"Dodo code: {_bot.DodoCode}").ConfigureAwait(false);
                }

                await Task.Delay(delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(intervalSeconds) : delay, token).ConfigureAwait(false);
            }
        }
    }
}

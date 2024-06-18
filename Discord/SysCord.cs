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
using static Discord.GatewayIntents;

namespace SysBot.ACNHOrders
{
    public sealed class SysCord
    {
        private readonly DiscordSocketClient _client;
        private readonly CrossBot Bot;
        public ulong Owner = ulong.MaxValue;
        public bool Ready = false;

        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public SysCord(CrossBot bot)
        {
            Bot = bot;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                LogLevel = LogSeverity.Info,
                GatewayIntents = Guilds | GuildMessages | DirectMessages | GuildMembers | MessageContent,
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                LogLevel = LogSeverity.Info,
                DefaultRunMode = RunMode.Sync,
                CaseSensitiveCommands = false,
            });

            _client.Log += Log;
            _commands.Log += Log;

            _services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var map = new ServiceCollection()
                .AddSingleton<CrossBot>(); // Example service

            return map.BuildServiceProvider();
        }

        private static Task Log(LogMessage msg)
        {
            Console.ForegroundColor = msg.Severity switch
            {
                LogSeverity.Critical => ConsoleColor.Red,
                LogSeverity.Error => ConsoleColor.Red,
                LogSeverity.Warning => ConsoleColor.Yellow,
                LogSeverity.Info => ConsoleColor.White,
                LogSeverity.Verbose => ConsoleColor.DarkGray,
                LogSeverity.Debug => ConsoleColor.DarkGray,
                _ => Console.ForegroundColor
            };

            var text = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
            Console.WriteLine($"{DateTime.Now,-19} {text}");
            Console.ResetColor();

            LogUtil.LogText($"SysCord: {text}");

            return Task.CompletedTask;
        }

        public async Task MainAsync(string apiToken, CancellationToken token)
        {
            await InitCommands().ConfigureAwait(false);

            await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);
            _client.Ready += ClientReady;

            await Task.Delay(5_000, token).ConfigureAwait(false);

            var game = Bot.Config.Name;
            if (!string.IsNullOrWhiteSpace(game))
                await _client.SetGameAsync(game).ConfigureAwait(false);

            var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
            Owner = app.Owner.Id;

            await MonitorStatusAsync(token).ConfigureAwait(false);
        }

        private async Task ClientReady()
        {
            if (Ready)
                return;
            Ready = true;

            await Task.Delay(1_000).ConfigureAwait(false);

            foreach (var cid in Bot.Config.LoggingChannels)
            {
                var c = (ISocketMessageChannel)_client.GetChannel(cid);
                if (c == null)
                {
                    Console.WriteLine($"{cid} is null or couldn't be found.");
                    continue;
                }
                static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
                void Logger(string msg, string identity) => c.SendMessageAsync(GetMessage(msg, identity));
                Action<string, string> l = Logger;
                LogUtil.Forwarders.Add(l);
            }

            await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task InitCommands()
        {
            var assembly = Assembly.GetExecutingAssembly();
            await _commands.AddModulesAsync(assembly, _services).ConfigureAwait(false);
            _client.MessageReceived += HandleMessageAsync;
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
                            return true;
                }

                if (channel is IMessageChannel textChannel)
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await Log(new LogMessage(LogSeverity.Error, "Exception", ex.Message, ex)).ConfigureAwait(false);
            }

            return false;
        }

        public async Task<bool> TrySpeakMessage(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await Log(new LogMessage(LogSeverity.Error, "Exception", ex.Message, ex)).ConfigureAwait(false);
            }

            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            if (arg is not SocketUserMessage msg)
                return;

            if (msg.Author.Id == _client.CurrentUser.Id || (!Bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            int pos = 0;
            if (msg.HasStringPrefix(Bot.Config.Prefix, ref pos))
            {
                bool handled = await TryHandleCommandAsync(msg, pos).ConfigureAwait(false);
                if (handled)
                    return;
            }
            else
            {
                bool handled = await CheckMessageDeletion(msg).ConfigureAwait(false);
                if (handled)
                    return;
            }

            await TryHandleMessageAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> CheckMessageDeletion(SocketUserMessage msg)
        {
            var context = new SocketCommandContext(_client, msg);

            var usrId = msg.Author.Id;
            if (!Globals.Bot.Config.DeleteNonCommands || context.IsPrivate || msg.Author.IsBot || Globals.Bot.Config.CanUseSudo(usrId) || msg.Author.Id == Owner)
                return false;
            if (Globals.Bot.Config.Channels.Count < 1 || !Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            var msgText = msg.Content;
            var mention = msg.Author.Mention;

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Possible spam detected in {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);

            await msg.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            await msg.Channel.SendMessageAsync($"{mention} - The order channels are for bot commands only.\nDeleted Message:```\n{msgText}\n```").ConfigureAwait(false);

            return true;
        }

        private static async Task TryHandleMessageAsync(SocketMessage msg)
        {
            if (msg.Attachments.Count > 0)
            {
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int pos)
        {
            var context = new SocketCommandContext(_client, msg);

            var mgr = Bot.Config;
            if (!Bot.Config.IgnoreAllPermissions)
            {
                if (!mgr.CanUseCommandUser(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You are not permitted to use this command.").ConfigureAwait(false);
                    return true;
                }
                if (!mgr.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != Owner && !mgr.CanUseSudo(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("You can't use that command here.").ConfigureAwait(false);
                    return true;
                }
            }

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Executing command from {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);
            var result = await _commands.ExecuteAsync(context, pos, _services).ConfigureAwait(false);

            if (result.Error == CommandError.UnknownCommand)
                return false;

            if (!result.IsSuccess)
                await msg.Channel.SendMessageAsync(result.ErrorReason).ConfigureAwait(false);
            return true;
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int Interval = 20;
            UserStatus state = UserStatus.Idle;

            while (!token.IsCancellationRequested)
            {
                var time = DateTime.Now;
                var lastLogged = LogUtil.LastLogged;
                var delta = time - lastLogged;
                var gap = TimeSpan.FromSeconds(Interval) - delta;

                if (gap <= TimeSpan.Zero)
                {
                    var idle = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Idle;
                    if (idle != state)
                    {
                        state = idle;
                        await _client.SetStatusAsync(state).ConfigureAwait(false);
                    }

                    if (Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                    {
                        await _client.SetGameAsync($"Dodo code: {Bot.DodoCode}").ConfigureAwait(false);
                    }

                    await Task.Delay(2000, token).ConfigureAwait(false);
                    continue;
                }

                var active = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Online;
                if (active != state)
                {
                    state = active;
                    await _client.SetStatusAsync(active).ConfigureAwait(false);
                }

                await Task.Delay(gap, token).ConfigureAwait(false);
            }
        }
    }
}

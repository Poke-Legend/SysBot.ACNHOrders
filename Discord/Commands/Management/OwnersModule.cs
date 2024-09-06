using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders.Discord.Commands.Management
{
    public class OwnerModule : ModuleBase<SocketCommandContext>
    {
        private const int GuildsPerPage = 25;
        private static readonly Color EmbedColor = new Color(52, 152, 219); // Blue (RGB)

        // Command to list all guilds the bot is part of
        [Command("listguilds")]
        [Alias("lg", "servers", "listservers")]
        [Summary("Lists all guilds the bot is part of.")]
        [RequireSudo]
        public async Task ListGuilds(int page = 1)
        {
            var guildCount = Context.Client.Guilds.Count;
            var totalPages = (int)Math.Ceiling(guildCount / (double)GuildsPerPage);
            page = Math.Max(1, Math.Min(page, totalPages));

            var guilds = Context.Client.Guilds
                .Skip((page - 1) * GuildsPerPage)
                .Take(GuildsPerPage);

            var embedBuilder = new EmbedBuilder()
                .WithTitle($"List of Guilds - Page {page}/{totalPages}")
                .WithDescription("Here are the guilds I'm currently in:")
                .WithColor(EmbedColor);

            foreach (var guild in guilds)
            {
                embedBuilder.AddField(guild.Name, $"ID: {guild.Id}", inline: true);
            }

            var dmChannel = await Context.User.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embedBuilder.Build());

            await ReplyAndDeleteAsync($"{Context.User.Mention}, I've sent you a DM with the list of guilds (Page {page}).");
        }

        // Command to leave the current server
        [Command("leave")]
        [Alias("bye")]
        [Summary("Leaves the current server.")]
        [RequireOwner]
        public async Task Leave()
        {
            await ReplyAndDeleteAsync("Goodbye.");
            await Context.Guild.LeaveAsync().ConfigureAwait(false);
        }

        // Command to leave a guild based on its ID
        [Command("leaveguild")]
        [Summary("Leaves guild based on supplied ID.")]
        [RequireOwner]
        public async Task LeaveGuild(string userInput)
        {
            if (!ulong.TryParse(userInput, out ulong id))
            {
                await ReplyAndDeleteAsync("Please provide a valid Guild ID.");
                return;
            }

            var guild = Context.Client.Guilds.FirstOrDefault(x => x.Id == id);
            if (guild is null)
            {
                await ReplyAndDeleteAsync($"Provided input ({userInput}) is not a valid guild ID or the bot is not in the specified guild.");
                return;
            }

            await ReplyAndDeleteAsync($"Leaving {guild.Name}.");
            await guild.LeaveAsync().ConfigureAwait(false);
        }

        // Command to leave all servers
        [Command("leaveall")]
        [Summary("Leaves all servers the bot is currently in.")]
        [RequireOwner]
        public async Task LeaveAll()
        {
            await ReplyAndDeleteAsync("Leaving all servers.");
            foreach (var guild in Context.Client.Guilds)
            {
                await guild.LeaveAsync().ConfigureAwait(false);
            }
        }

        // Command to send a DM to a specific user
        [Command("dm")]
        [Summary("Sends a direct message to a specified user.")]
        [RequireOwner]
        public async Task DMUserAsync(SocketUser user, [Remainder] string message)
        {
            var embed = new EmbedBuilder
            {
                Title = "Private Message from the Bot Owner!",
                Description = message,
                Color = EmbedColor,
                Timestamp = DateTimeOffset.Now,
                ThumbnailUrl = "https://raw.githubusercontent.com/Poke-Legend/ACNH-Images/main/Images/Mail/MailBox.png"
            };

            var result = await SendMessageAsync(user, embed, Context.Message.Attachments);
            await ReplyAndDeleteAsync(result);
        }

        [Command("addSudo")]
        [RequireOwner]  // Only allow the bot owner to add sudo users
        public async Task AddSudoAsync([Remainder] string userInput)
        {
            // Try to find the user either by mention or by ID
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            // If no mentioned user, try to parse the input as a user ID
            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                user = Context.Client.GetUser(userId);
            }

            // If still no user found, return an error message
            if (user == null)
            {
                await ReplyAsync("User not found. Please mention a valid user or provide a valid user ID.");
                return;
            }

            // Add the user to the sudo list and save
            Globals.Manager.AddSudo(user.Id);
            await ReplyAsync($"{user.Username} has been added to the sudo list.").ConfigureAwait(false);
        }

        [Command("removeSudo")]
        [RequireOwner]  // Only allow the bot owner to remove sudo users
        public async Task RemoveSudoAsync([Remainder] string userInput)
        {
            // Try to find the user either by mention or by ID
            SocketUser? user = Context.Message.MentionedUsers.FirstOrDefault();

            // If no mentioned user, try to parse the input as a user ID
            if (user == null && ulong.TryParse(userInput, out ulong userId))
            {
                user = Context.Client.GetUser(userId);
            }

            // If still no user found, return an error message
            if (user == null)
            {
                await ReplyAsync("User not found. Please mention a valid user or provide a valid user ID.");
                return;
            }

            // Remove the user from the sudo list and save
            Globals.Manager.RemoveSudo(user.Id);
            await ReplyAsync($"{user.Username} has been removed from the sudo list.").ConfigureAwait(false);
        }

        [Command("listSudo")]
        [RequireOwner]  // Only allow the bot owner to view the sudo list
        public async Task ListSudoAsync()
        {
            var sudoUsers = Globals.Manager.GetAllSudoUsers();
            if (!sudoUsers.Any())
            {
                await ReplyAsync("No users have sudo privileges.").ConfigureAwait(false);
                return;
            }

            var userList = string.Join("\n", sudoUsers.Select(id => $"<@{id}>"));
            await ReplyAsync($"Sudo users:\n{userList}").ConfigureAwait(false);
        }

        // Helper method to get a user reference
        private SudoUser GetReference(SocketUser user)
        {
            return new SudoUser
            {
                ID = user.Id,
                Username = user.Username // This ensures the Username is always initialized
            };
        }

        private async Task<string> SendMessageAsync(SocketUser user, EmbedBuilder embed, IReadOnlyCollection<Attachment> attachments)
        {
            try
            {
                var dmChannel = await user.CreateDMChannelAsync();

                if (attachments.Any())
                {
                    using var httpClient = new HttpClient();
                    foreach (var attachment in attachments)
                    {
                        var stream = await httpClient.GetStreamAsync(attachment.Url);
                        var file = new FileAttachment(stream, attachment.Filename);
                        await dmChannel.SendFileAsync(file, embed: embed.Build());
                    }
                }
                else
                {
                    await dmChannel.SendMessageAsync(embed: embed.Build());
                }

                return $"Message successfully sent to {user.Username}.";
            }
            catch (Exception ex)
            {
                return $"Failed to send message to {user.Username}. Error: {ex.Message}";
            }
        }

        private async Task ReplyAndDeleteAsync(string message)
        {
            var userMessage = await ReplyAsync(message);
            await Task.Delay(2000);
            await userMessage.DeleteAsync().ConfigureAwait(false);
        }
    }

    public class SudoUser
    {
        public ulong ID { get; set; }
        public string Username { get; set; } = string.Empty; // Initialize with default value
    }

}

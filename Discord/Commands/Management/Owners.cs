﻿using System;
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

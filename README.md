# Pokémon Legends Animal Crossing: New Horizons Sysbot

## Overview

**SysBot.ACNHOrders** is a fully automated, queue-based order bot that allows players to easily inject item orders directly onto their island's map in *Animal Crossing: New Horizons*. From queuing up orders to item pickup, everything is handled seamlessly. The bot manages all necessary processes, including fetching dodo codes, managing gates, and even automating Tom Nook's or Isabelle's morning announcements. It also includes advanced features such as connection logging, item dropping, map refreshing, and session recovery after network interruptions.

## Features

### New Systems

1. **Server Ban System**  
   Efficiently manage banned users across the server, ensuring smooth and secure operations.

2. **Broadcasting System**  
   Easily send important announcements to all users or specific channels.

3. **Ping System**  
   Allows users to ping the bot and receive a quick response, confirming it's active and functional.

4. **MysteryOrder System**  
   Adds an element of surprise by letting users receive randomized item orders.

5. **Embeds System**  
   Enhance the clarity of bot messages with rich, visually appealing embeds.

6. **Channel Automatic Changes**  
   Automatically updates designated channels from an X mark to a check mark, streamlining channel statuses.

### New Commands

1. **LeaveGuild**  
   Command the bot to leave a specified Discord server.

2. **LeaveAll**  
   Instruct the bot to leave all the Discord servers it's part of.

3. **Leave**  
   Make the bot leave the current Discord server.

4. **ListGuild**  
   Sends a list of all the servers the bot is in via DM to the user.

5. **AddChannel**  
   Whitelist a specific channel ID for the Channel Monitor system.

6. **RemoveChannel**  
   Remove a whitelisted channel ID from the Channel Monitor system.

### Donation Module (New!)

1. **setlink Command**  
   Admin-only command to add a donation link, which will be saved in `donation.json` for display.

2. **Donation Command**  
   Allows users to view the saved donation link, providing a way for users to support the bot's operations.

## Getting Started

### Prerequisites

- **sys-botbase client**: Required to enable remote control and automation of Nintendo Switch consoles.
- **Discord.Net**: A .NET library for interacting with Discord, available through NuGet.
- **Animal Crossing API logic**: Utilizes NHSE for managing in-game data interactions.

## Acknowledgments

A huge thank you to the following contributors:

- **kwsch**: For their foundational work on the original project.
- **Red**: For contributions like the original Dodo-fetch code, pointer logic, and more.
- **CodeHedge**: For developing the Order Embeds Logic System, Broadcasting System, and MysteryOrder System.
- **Berichan**: For providing the source code and contributing to numerous features.

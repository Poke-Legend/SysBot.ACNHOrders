# Pokémon Legends Animal Crossing New Horizons Sysbot

## Overview

**SysBot.ACNHOrders** is a fully automated, queue-based order bot designed to inject item orders directly onto your island's map in *Animal Crossing: New Horizons*. Players can queue their orders, pick them up, and leave. All processes—including dodo fetching, gate management, and even handling Tom Nook's or Isabelle's morning announcements—are fully automated. The bot also provides advanced connection logging, item dropping, map refreshing, and session restoration after network disruptions.

## Features

### New Systems

1. **Server Ban System**: 
   - Manage banned users with an integrated system that handles bans across the server.

2. **Broadcasting System**: 
   - Send important messages to all users or specific channels.

3. **Ping System**: 
   - Users can ping the bot, and it will respond quickly to confirm it’s active.

4. **MysteryOrder System**: 
   - Allows users to receive randomized item orders, adding an element of surprise.

5. **Embeds System**: 
   - Enhance bot messages with rich, visually appealing embeds for better communication.

### New Commands

1. **LeaveGuild**: 
   - Command for the bot to leave a specific Discord server.

2. **LeaveAll**: 
   - Command for the bot to leave all Discord servers it is part of.

3. **Leave**: 
   - Command to make the bot leave the current Discord server.

4. **ListGuild**: 
   - Sends a list of all servers the bot is in directly to the user’s DM.

## Getting Started

### Prerequisites

- **sys-botbase client**: Enables remote control automation of Nintendo Switch consoles.
- **Discord.Net**: A .NET library for interacting with Discord, managed via NuGet.
- **Animal Crossing API logic**: Provided by NHSE for integration with game data.

## Acknowledgments

Special thanks to:

- **kwsch**: For the original project and foundational work.
- **Red**: For the original Dodo-fetch code, pointer logic, and numerous contributions.
- **CodeHedge**: For Order Embeds Logic System.
- **Berichan**: For providing the source code and numerous contributions.

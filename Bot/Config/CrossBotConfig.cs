using System;
using System.Collections.Generic;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    /// <summary>
    /// Represents all configuration settings for the CrossBot, including
    /// Discord-related options, feature flags, and inherited Switch connection settings.
    /// </summary>
    [Serializable]
    public sealed record CrossBotConfig : SwitchConnectionConfig
    {
        /// <summary>
        /// Global set of sudo user IDs, allowing them extra permissions.
        /// </summary>
        public HashSet<ulong> GlobalSudoList { get; set; } = new();

        public bool CanUseSudo(ulong userId) => GlobalSudoList.Contains(userId);

        #region Discord

        /// <summary>
        /// Whether the bot is currently accepting commands.
        /// </summary>
        public bool AcceptingCommands { get; set; } = true;

        /// <summary>
        /// The "friendly" name (presence) shown by the Discord bot.
        /// </summary>
        public string Name { get; set; } = "CrossBot";

        /// <summary>
        /// Bot's Discord token (must be set to run on Discord).
        /// </summary>
        public string Token { get; set; } = "DISCORD_TOKEN";

        /// <summary>
        /// The default prefix for commands, e.g. "$".
        /// </summary>
        public string Prefix { get; set; } = "$";

        /// <summary>
        /// Channels (IDs) in which the bot is allowed to respond.
        /// </summary>
        public List<ulong> Channels { get; set; } = new();

        /// <summary>
        /// Users (IDs) who can use the bot if permission-limited.
        /// </summary>
        public List<ulong> Users { get; set; } = new();

        /// <summary>
        /// Channels (IDs) where logs or notifications are sent by the bot.
        /// </summary>
        public List<ulong> LoggingChannels { get; set; } = new();

        /// <summary>
        /// If true, the bot ignores permission checks (everyone can use commands).
        /// </summary>
        public bool IgnoreAllPermissions { get; set; } = false;

        #endregion

        #region Features

        /// <summary>
        /// If true, the bot won't create the console bot instance in BotRunner. 
        /// This can affect whether the bot auto-restarts.
        /// </summary>
        public bool SkipConsoleBotCreation { get; set; }

        /// <summary>
        /// Requires a valid inventory metadata for item operations.
        /// </summary>
        public bool RequireValidInventoryMetadata { get; set; } = true;

        /// <summary>
        /// Whether the bot allows dropping items in ACNH.
        /// </summary>
        public bool AllowDrop { get; set; } = true;

        /// <summary>
        /// Holds settings related to dropping items.
        /// </summary>
        public DropBotConfig DropConfig { get; set; } = new();

        /// <summary>
        /// Holds settings related to ordering items.
        /// </summary>
        public OrderBotConfig OrderConfig { get; set; } = new();

        /// <summary>
        /// Settings related to restoring a Dodo code or reloading the session.
        /// </summary>
        public DodoRestoreConfig DodoModeConfig { get; set; } = new();

        /// <summary>
        /// Whether to allow a "clean" operation (e.g., clearing dropped items).
        /// </summary>
        public bool AllowClean { get; set; }

        /// <summary>
        /// Whether to allow item lookups or other read-only queries.
        /// </summary>
        public bool AllowLookup { get; set; }

        /// <summary>
        /// Anchor filename used for some internal bot logic (e.g., item anchors).
        /// </summary>
        public string AnchorFilename { get; set; } = "Anchors.bin";

        /// <summary>
        /// If true, forcibly re-creates anchor data on startup.
        /// </summary>
        public bool ForceUpdateAnchors { get; set; } = false;

        /// <summary>
        /// Coordinates on the ACNH map where items are placed.
        /// </summary>
        public int MapPlaceX { get; set; } = -1;
        public int MapPlaceY { get; set; } = -1;

        /// <summary>
        /// Number of bytes to read at once when pulling map data.
        /// </summary>
        public int MapPullChunkSize { get; set; } = 4096;

        /// <summary>
        /// If true, bot will delete any non-command messages in certain channels.
        /// </summary>
        public bool DeleteNonCommands { get; set; } = false;

        /// <summary>
        /// Additional delay (in ms) after pressing dialogue buttons.
        /// </summary>
        public int DialogueButtonPressExtraDelay { get; set; } = 0;

        /// <summary>
        /// Additional wait time (in ms) before restarting the game, if triggered.
        /// </summary>
        public int RestartGameWait { get; set; } = 0;

        /// <summary>
        /// Extra time (in ms) to wait for a connection to settle.
        /// </summary>
        public int ExtraTimeConnectionWait { get; set; } = 1000;

        /// <summary>
        /// Extra time (in ms) before entering airport if needed.
        /// </summary>
        public int ExtraTimeEnterAirportWait { get; set; } = 0;

        /// <summary>
        /// Attempts to mitigate issues where dialogues warp the player unexpectedly.
        /// </summary>
        public bool AttemptMitigateDialogueWarping { get; set; } = false;

        /// <summary>
        /// Uses older logic for retrieving a Dodo code.
        /// </summary>
        public bool LegacyDodoCodeRetrieval { get; set; } = false;

        /// <summary>
        /// Experimental freeze approach to Dodo code retrieval.
        /// </summary>
        public bool ExperimentalFreezeDodoCodeRetrieval { get; set; } = false;

        /// <summary>
        /// Experimental approach to put Switch screen to sleep on idle.
        /// </summary>
        public bool ExperimentalSleepScreenOnIdle { get; set; } = false;

        /// <summary>
        /// Allows injecting custom villagers onto the island.
        /// </summary>
        public bool AllowVillagerInjection { get; set; } = true;

        /// <summary>
        /// If true, hides player arrival names in logs or announcements.
        /// </summary>
        public bool HideArrivalNames { get; set; } = false;

        /// <summary>
        /// Directory containing .nhl files for field layers.
        /// </summary>
        public string FieldLayerNHLDirectory { get; set; } = "nhl";

        /// <summary>
        /// The name or ID for the field layer.
        /// </summary>
        public string FieldLayerName { get; set; } = "name";

        /// <summary>
        /// If true, known abusers are allowed to use the bot (not recommended).
        /// </summary>
        public bool AllowKnownAbusers { get; set; } = false;

        /// <summary>
        /// If true, tries to skip system updates on the console if prompted.
        /// </summary>
        public bool AvoidSystemUpdate { get; set; } = true;

        /// <summary>
        /// Links for images used when the bot responds to a "Hi" command.
        /// </summary>
        public string[] HiCommandImageLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Links for images used when an order is initializing.
        /// </summary>
        public string[] OrderInitializingLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Images for when an order is ready.
        /// </summary>
        public string[] OrderReadyImageLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Images for when an order is canceled.
        /// </summary>
        public string[] OrderCanceledImageLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Images for when an order is completed.
        /// </summary>
        public string[] OrderCompletedImageLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Images for broadcast messages or announcements.
        /// </summary>
        public string[] BroadcastImageLinks { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Configuration for SignalR-based web interactions (if used).
        /// </summary>
        public WebConfig SignalrConfig { get; set; } = new();

        #endregion

        /// <summary>
        /// Checks if a user (by ID) is allowed to issue commands 
        /// (e.g., if the user list is empty, everyone is allowed).
        /// </summary>
        public bool CanUseCommandUser(ulong authorId) => Users.Count == 0 || Users.Contains(authorId);

        /// <summary>
        /// Checks if the bot can be used in a specific channel (by ID).
        /// </summary>
        public bool CanUseCommandChannel(ulong channelId) => Channels.Count == 0 || Channels.Contains(channelId);
    }
}

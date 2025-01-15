using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    /// <summary>
    /// Primary ACNH order bot logic that manages item drops, dodo code generation, 
    /// map overrides, villager injections, and more.
    /// </summary>
    public sealed class CrossBot : SwitchRoutineExecutor<CrossBotConfig>
    {
        private ConcurrentQueue<IACNHOrderNotifier<Item>> Orders => QueueHub.CurrentInstance.Orders;

        private uint InventoryOffset { get; set; } = (uint)OffsetHelper.InventoryOffset;

        // Public request queues that can be filled externally
        public readonly ConcurrentQueue<ItemRequest> Injections = new();
        public readonly ConcurrentQueue<SpeakRequest> Speaks = new();
        public readonly ConcurrentQueue<VillagerRequest> VillagerInjections = new();
        public readonly ConcurrentQueue<MapOverrideRequest> MapOverrides = new();
        public readonly ConcurrentQueue<TurnipRequest> StonkRequests = new();

        // Key helpers and state objects
        public readonly PocketInjectorAsync PocketInjector;
        public readonly DodoPositionHelper DodoPosition;
        public readonly AnchorHelper Anchors;
        public readonly VisitorListHelper VisitorList;
        public readonly ISwitchConnectionAsync SwitchConnection;
        public readonly ExternalMapHelper ExternalMap;
        public readonly DropBotState State;
        public readonly DodoDraw? DodoImageDrawer;
        public readonly ConcurrentBag<IDodoRestoreNotifier> DodoNotifiers = new();

        // A "dummy" order for forced dodo code refresh
        public readonly DummyOrder<Item> DummyRequest = new();

        // Public properties that track map & session data
        public MapTerrainLite Map { get; private set; } = new MapTerrainLite(new byte[MapGrid.MapTileCount32x32 * Item.SIZE]);
        public VillagerHelper Villagers { get; private set; } = VillagerHelper.Empty;

        public TimeBlock LastTimeState { get; private set; } = new();
        public bool CleanRequested { private get; set; }
        public bool RestoreRestartRequested { private get; set; }
        public bool GameIsDirty { get; set; } = true; // If crashed or previous session ended incorrectly

        // Basic metadata
        public string DodoCode { get; set; } = "No code set yet.";
        public string VisitorInfo { get; set; } = "No visitor info yet.";
        public string TownName { get; set; } = "No town name yet.";
        public string CLayer { get; set; } = "No layer set yet.";
        public string DisUserID { get; set; } = string.Empty;

        // Tracks last arrival
        public string LastArrival { get; private set; } = string.Empty;
        public string LastArrivalIsland { get; private set; } = string.Empty;

        // Current user ID & name who is ordering or arrived
        public ulong CurrentUserId { get; set; } = default!;
        public string CurrentUserName { get; set; } = string.Empty;

        // System state
        public ulong ChatAddress { get; set; } = 0;
        public int ChargePercent { get; set; } = 100;
        public DateTime LastDodoFetchTime { get; private set; } = DateTime.Now;

        private readonly byte[] MaxTextSpeed = { 3 }; // used for conversation speed

        public CrossBot(CrossBotConfig cfg) : base(cfg)
        {
            State = new DropBotState(cfg.DropConfig);
            Anchors = new AnchorHelper(Config.AnchorFilename);

            if (Connection is ISwitchConnectionAsync asyncConn)
                SwitchConnection = asyncConn;
            else
                throw new Exception("Connection is null or not asynchronous.");

            // If using SwitchSocketAsync, set the maximum chunk size for map data reading/writing
            if (Connection is SwitchSocketAsync ssa)
                ssa.MaximumTransferSize = cfg.MapPullChunkSize;

            // If dodo.png & dodo.ttf exist, enable Dodo code image creation
            if (File.Exists("dodo.png") && File.Exists("dodo.ttf"))
                DodoImageDrawer = new DodoDraw(Config.DodoModeConfig.DodoFontPercentageSize);

            DodoPosition = new DodoPositionHelper(this);
            VisitorList = new VisitorListHelper(this);
            PocketInjector = new PocketInjectorAsync(SwitchConnection, InventoryOffset);

            // External Map usage
            var layerFileNameNoExt = File.Exists(Config.DodoModeConfig.LoadedNHLFilename)
                ? File.ReadAllText(Config.DodoModeConfig.LoadedNHLFilename)
                : string.Empty;
            var fileName = string.IsNullOrWhiteSpace(layerFileNameNoExt)
                ? string.Empty
                : layerFileNameNoExt + ".nhl";
            ExternalMap = new ExternalMapHelper(cfg, fileName);
        }

        /// <summary>
        /// Soft stop simply disables the bot from accepting new commands.
        /// </summary>
        public override void SoftStop()
            => Config.AcceptingCommands = false;

        /// <summary>
        /// Main entry point for the CrossBot's loop. Validates offsets, loads map & anchors, 
        /// then processes either DodoRestoreLoop or OrderLoop as appropriate.
        /// </summary>
        public override async Task MainLoop(CancellationToken token)
        {
            // Validate map spawn vector
            if (Config.MapPlaceX < 0 || Config.MapPlaceX >= (MapGrid.AcreWidth * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceX} is not valid for {nameof(Config.MapPlaceX)}. Exiting!", Config.IP);
                return;
            }

            if (Config.MapPlaceY < 0 || Config.MapPlaceY >= (MapGrid.AcreHeight * 32))
            {
                LogUtil.LogInfo($"{Config.MapPlaceY} is not valid for {nameof(Config.MapPlaceY)}. Exiting!", Config.IP);
                return;
            }

            // Disconnect the virtual controller initially
            LogUtil.LogInfo("Detaching controller on startup as first interaction.", Config.IP);
            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            // Hide the on-screen "blocker" and ensure the screen is awake
            await SetScreenCheck(false, token).ConfigureAwait(false);

            // Check sys-botbase version
            await Task.Delay(100, token).ConfigureAwait(false);
            LogUtil.LogInfo("Attempting to get version. Please wait...", Config.IP);
            string version = await SwitchConnection.GetVersionAsync(token).ConfigureAwait(false);
            LogUtil.LogInfo($"sys-botbase version identified as: {version}", Config.IP);

            // Determine the actual inventory offset (player offset)
            InventoryOffset = await GetCurrentPlayerOffset((uint)OffsetHelper.InventoryOffset,
                                                           (uint)OffsetHelper.PlayerSize, token)
                                   .ConfigureAwait(false);
            PocketInjector.WriteOffset = InventoryOffset;

            // Validate the inventory offset
            LogUtil.LogInfo("Checking inventory offset for validity.", Config.IP);
            bool validInventory = await GetIsPlayerInventoryValid(InventoryOffset, token).ConfigureAwait(false);
            if (!validInventory)
            {
                LogUtil.LogInfo($"Inventory read from 0x{InventoryOffset:X8} is invalid.", Config.IP);
                if (Config.RequireValidInventoryMetadata)
                {
                    LogUtil.LogInfo("Exiting due to invalid inventory metadata requirement.", Config.IP);
                    return;
                }
            }

            // Ensure "UserOrder" folder exists if needed
            if (!Directory.Exists("UserOrder"))
            {
                Directory.CreateDirectory("UserOrder");
            }

            // Load a default .nhl layer on bot startup if relevant
            await LoadInitialLayerAsync(token).ConfigureAwait(false);

            // Pull the original map items & terrain data
            await ReadOriginalMapAsync(token).ConfigureAwait(false);

            // Read Town Name
            LogUtil.LogInfo("Reading Town Name. Please wait...", Config.IP);
            var townNameBytes = await Connection.ReadBytesAsync(
                (uint)OffsetHelper.getTownNameAddress(InventoryOffset), 0x14, token
            ).ConfigureAwait(false);
            TownName = Encoding.Unicode.GetString(townNameBytes).TrimEnd('\0');

            if (Globals.Bot.Config.FieldLayerName == "name")
                CLayer = TownName;

            VisitorList.SetTownName(TownName);
            LogUtil.LogInfo($"Town name set to {TownName}", Config.IP);

            // Pull villager data
            Villagers = await VillagerHelper.GenerateHelper(this, token).ConfigureAwait(false);

            // Pull in-game time
            var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token)
                                            .ConfigureAwait(false);
            LastTimeState = timeBytes.ToClass<TimeBlock>();
            LogUtil.LogInfo($"Started at in-game time: {LastTimeState}", Config.IP);

            if (Config.ForceUpdateAnchors)
                LogUtil.LogInfo("Force update anchors is set to true (no further anchor usage?).", Config.IP);

            LogUtil.LogInfo("Successfully connected to bot. Starting main loop!", Config.IP);

            // If the config is set to a "limited dodo restore" mode, skip normal order logic
            if (Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                if (Config.DodoModeConfig.FreezeMap && Config.DodoModeConfig.RefreshMap)
                {
                    LogUtil.LogInfo("You cannot freeze and refresh the map simultaneously. Exiting...", Config.IP);
                    return;
                }

                if (Config.DodoModeConfig.FreezeMap)
                {
                    LogUtil.LogInfo("Freezing map, please wait...", Config.IP);
                    await SwitchConnection.FreezeValues(
                        (uint)OffsetHelper.FieldItemStart,
                        Map.StartupBytes,
                        ConnectionHelper.MapChunkCount, token
                    ).ConfigureAwait(false);
                }

                LogUtil.LogInfo(
                    "Orders not accepted in dodo restore mode! Ensure controllers are docked! Starting dodo restore loop...",
                    Config.IP
                );
                try
                {
                    while (!token.IsCancellationRequested)
                        await DodoRestoreLoop(false, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    LogUtil.LogError($"Dodo restore loop ended with error: {e.Message}\r\n{e.StackTrace}", Config.IP);
                }
                return;
            }

            // Normal order loop
            try
            {
                while (!token.IsCancellationRequested)
                    await OrderLoop(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogUtil.LogError($"Order loop ended with error: {e.Message}\r\n{e.StackTrace}", Config.IP);
                // Exiting so the BotRunner can catch & possibly restart
            }
        }

        #region Dodo Restore Logic

        /// <summary>
        /// Handles restoring a new Dodo code session if the game or connection crashed, 
        /// or if the user explicitly triggers a forced refresh.
        /// </summary>
        private async Task DodoRestoreLoop(bool immediateRestart, CancellationToken token)
        {
            await EnsureAnchorsAreInitialised(token);
            await VisitorList.UpdateNames(token).ConfigureAwait(false);

            if (File.Exists(Config.DodoModeConfig.LoadedNHLFilename))
            {
                string layerNameNoExt = File.ReadAllText(Config.DodoModeConfig.LoadedNHLFilename);
                await AttemptEchoHook(
                    $"[Restarted] {TownName} was last loaded with layer: {layerNameNoExt}.nhl",
                    Config.DodoModeConfig.EchoIslandUpdateChannels,
                    token, true
                ).ConfigureAwait(false);
            }

            bool hardCrash = immediateRestart;
            if (!immediateRestart)
            {
                // Grab the existing dodo code from memory
                byte[] dodoBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.DodoAddress, 5, token)
                                                   .ConfigureAwait(false);
                DodoCode = Encoding.UTF8.GetString(dodoBytes, 0, 5);

                if (DodoPosition.IsDodoValid(DodoCode) && Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                {
                    await AttemptEchoHook(
                        $"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] The Dodo code for {TownName} updated: {DodoCode}.",
                        Config.DodoModeConfig.EchoDodoChannels,
                        token
                    ).ConfigureAwait(false);
                }

                NotifyDodo(DodoCode);

                // If max bells is enabled, set them
                if (Config.DodoModeConfig.MaxBells)
                {
                    Globals.Bot.StonkRequests.Enqueue(new TurnipRequest("null", 999999999));
                    await AttemptEchoHook(
                        "All turnip values successfully set to Max Bells!",
                        Config.DodoModeConfig.EchoDodoChannels,
                        token
                    ).ConfigureAwait(false);
                }

                await SaveDodoCodeToFile(token).ConfigureAwait(false);

                // Wait while the session is active
                while (await IsNetworkSessionActive(token).ConfigureAwait(false))
                {
                    await Task.Delay(2_000, token).ConfigureAwait(false);

                    ChargePercent = await SwitchConnection.GetChargePercentAsync(token).ConfigureAwait(false);

                    if (RestoreRestartRequested)
                    {
                        RestoreRestartRequested = false;
                        await ResetFiles(token).ConfigureAwait(false);
                        await AttemptEchoHook(
                            $"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Please wait for the new dodo code for {TownName}.",
                            Config.DodoModeConfig.EchoDodoChannels,
                            token
                        ).ConfigureAwait(false);

                        await DodoRestoreLoop(true, token).ConfigureAwait(false);
                        return;
                    }

                    NotifyState(GameState.Active);

                    var owState = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                                    .ConfigureAwait(false);
                    if (Config.DodoModeConfig.RefreshMap &&
                        (owState == OverworldState.UserArriveLeaving || owState == OverworldState.Loading))
                    {
                        // Only refresh map when someone is leaving/arriving or changing buildings
                        await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData, token)
                            .ConfigureAwait(false);
                    }

                    // If MashB is enabled, spam B to skip text
                    if (Config.DodoModeConfig.MashB)
                    {
                        for (int i = 0; i < 5; i++)
                            await Click(SwitchButton.B, 200, token).ConfigureAwait(false);
                    }

                    // Update in-game time
                    var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token)
                                                    .ConfigureAwait(false);
                    LastTimeState = timeBytes.ToClass<TimeBlock>();

                    // Process any drop logic
                    await DropLoop(token).ConfigureAwait(false);

                    // Check for departed visitors
                    var diffs = await VisitorList.UpdateNames(token).ConfigureAwait(false);
                    if (Config.DodoModeConfig.EchoArrivalChannels.Count > 0)
                    {
                        foreach (var diff in diffs)
                            if (!diff.Arrived)
                                await AttemptEchoHook(
                                    $"> [{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] 🛫 {diff.Name} departed from {TownName}",
                                    Config.DodoModeConfig.EchoArrivalChannels,
                                    token
                                ).ConfigureAwait(false);
                    }

                    // Check for new arrivals
                    if (await IsArriverNew(token).ConfigureAwait(false))
                    {
                        if (Config.DodoModeConfig.EchoArrivalChannels.Count > 0)
                        {
                            string arrivalMsg =
                                $"> [{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] 🛬 {LastArrival} from {LastArrivalIsland} is joining {TownName}."
                                + (Config.DodoModeConfig.PostDodoCodeWithNewArrivals ? $" Dodo code: {DodoCode}." : string.Empty);
                            await AttemptEchoHook(arrivalMsg, Config.DodoModeConfig.EchoArrivalChannels, token)
                                .ConfigureAwait(false);
                        }

                        // Log arrival
                        var nid = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNID, 8, token)
                                                  .ConfigureAwait(false);
                        var islandId = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageId, 4, token)
                                                       .ConfigureAwait(false);
                        try
                        {
                            var newnid = BitConverter.ToUInt64(nid, 0);
                            var newnislid = BitConverter.ToUInt32(islandId, 0);
                            LogUtil.LogInfo($"Arrival logged: NID={newnid}, TownID={newnislid}, details=Treasure island arrival",
                                Config.IP);
                        }
                        catch
                        {
                            // ignored
                        }

                        // Wait a minute, then clear the last arrival name
                        await Task.Delay(60_000, token).ConfigureAwait(false);
                        await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token)
                                        .ConfigureAwait(false);
                        LastArrival = string.Empty;
                    }

                    await SaveVisitorsToFile(token).ConfigureAwait(false);
                    await DropLoop(token).ConfigureAwait(false);

                    // Check villager injections
                    if (VillagerInjections.TryDequeue(out var vil))
                        await Villagers.InjectVillager(vil, token).ConfigureAwait(false);

                    // Update villagers
                    var lostVillagers = await Villagers.UpdateVillagers(token).ConfigureAwait(false);
                    if (Config.DodoModeConfig.ReinjectMovedOutVillagers && lostVillagers != null)
                    {
                        foreach (var lv in lostVillagers)
                        {
                            if (!lv.Value.StartsWith("non"))
                            {
                                var vilData = VillagerResources.GetVillager(lv.Value);
                                var displayName = GameInfo.Strings.GetVillager(lv.Value);
                                VillagerInjections.Enqueue(new VillagerRequest("REINJECT", vilData, (byte)lv.Key, displayName));
                            }
                        }
                    }
                    await SaveVillagersToFile(token).ConfigureAwait(false);

                    // Check for map overrides
                    if (MapOverrides.TryDequeue(out var mapRequest) || ExternalMap.CheckForCycle(out mapRequest))
                    {
                        if (mapRequest != null)
                        {
                            var tempMap = new MapTerrainLite(mapRequest.Item, Map.StartupTerrain, Map.StartupAcreParams)
                            {
                                SpawnX = Config.MapPlaceX,
                                SpawnY = Config.MapPlaceY
                            };
                            Map = tempMap;

                            if (!Config.DodoModeConfig.FreezeMap)
                            {
                                await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData,
                                    token, true).ConfigureAwait(false);
                            }
                            else
                            {
                                await SwitchConnection.FreezeValues(
                                    (uint)OffsetHelper.FieldItemStart,
                                    Map.StartupBytes,
                                    ConnectionHelper.MapChunkCount,
                                    token
                                ).ConfigureAwait(false);
                            }

                            string newLayerName = Path.GetFileNameWithoutExtension(mapRequest.OverrideLayerName);
                            await AttemptEchoHook(
                                $"{TownName} switched to item layer: {mapRequest.OverrideLayerName}",
                                Config.DodoModeConfig.EchoIslandUpdateChannels, token
                            ).ConfigureAwait(false);
                            await SaveLayerNameToFile(newLayerName, token).ConfigureAwait(false);
                        }
                    }

                    // Check for auto new dodo code if only host is left on the island
                    if (Config.DodoModeConfig.AutoNewDodoTimeMinutes > -1)
                    {
                        double minutesSinceFetch = (DateTime.Now - LastDodoFetchTime).TotalMinutes;
                        if (minutesSinceFetch >= Config.DodoModeConfig.AutoNewDodoTimeMinutes &&
                            VisitorList.VisitorCount == 1)
                        {
                            // Time to generate a new code
                            RestoreRestartRequested = true;
                        }
                    }
                } // end while session active

                // If we broke out, we likely crashed
                if (Config.DodoModeConfig.EchoDodoChannels.Count > 0)
                {
                    string crashMsg =
                        $"[{DateTime.Now:yyyy-MM-dd hh:mm:ss tt}] Crash detected on {TownName}. " +
                        "Please wait while I get a new Dodo code.";
                    await AttemptEchoHook(crashMsg, Config.DodoModeConfig.EchoDodoChannels, token)
                        .ConfigureAwait(false);
                }
                NotifyState(GameState.Fetching);
                LogUtil.LogInfo($"Crash detected on {TownName}, awaiting overworld to fetch new dodo.", Config.IP);

                await ResetFiles(token).ConfigureAwait(false);
                await Task.Delay(5_000, token).ConfigureAwait(false);

                // Clear dodo code from memory
                await Connection.WriteBytesAsync(new byte[5], (uint)OffsetHelper.DodoAddress, token)
                                .ConfigureAwait(false);

                var startTime = DateTime.Now;
                LogUtil.LogInfo("Begin overworld wait loop.", Config.IP);
                while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                         .ConfigureAwait(false) != OverworldState.Overworld)
                {
                    await Task.Delay(1_000, token).ConfigureAwait(false);
                    await Click(SwitchButton.B, 100, token).ConfigureAwait(false);

                    if (Math.Abs((DateTime.Now - startTime).TotalSeconds) > 45)
                    {
                        LogUtil.LogError($"Hard crash detected on {TownName}, restarting game.", Config.IP);
                        hardCrash = true;
                        break;
                    }
                }
                LogUtil.LogInfo("End overworld wait loop.", Config.IP);
            }

            // Execute a "dummy" order to refresh dodo code
            var newOrderResult = await ExecuteOrderStart(DummyRequest, true, hardCrash, token).ConfigureAwait(false);
            if (newOrderResult != OrderResult.Success)
            {
                LogUtil.LogError($"Dodo restore failed with error: {newOrderResult}. Restarting game...", Config.IP);
                await DodoRestoreLoop(true, token).ConfigureAwait(false);
                return;
            }

            await SaveDodoCodeToFile(token).ConfigureAwait(false);
            LogUtil.LogInfo(
                $"Dodo restore successful. New dodo for {TownName} is {DodoCode} " +
                $"(saved to {Config.DodoModeConfig.DodoRestoreFilename}).",
                Config.IP
            );

            if (Config.DodoModeConfig.RefreshMap)
            {
                // Clean map if needed
                await ClearMapAndSpawnInternally(null, Map, Config.DodoModeConfig.RefreshTerrainData, token, true)
                    .ConfigureAwait(false);
            }
        }

        #endregion

        #region Order Handling

        /// <summary>
        /// Repeatedly processes any queued orders if accepting commands. 
        /// Handles main item logic and checks for session or time-of-day changes.
        /// </summary>
        private async Task OrderLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                // Just wait 1s if bot is not currently accepting commands
                await Task.Delay(1_000, token).ConfigureAwait(false);
                await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                return;
            }

            await EnsureAnchorsAreInitialised(token);

            // If there's a queued order, process it
            if (Orders.TryDequeue(out var order) && !order.SkipRequested)
            {
                var result = await ExecuteOrder(order, token).ConfigureAwait(false);

                // Cleanup user-specific state
                LogUtil.LogInfo($"Exited order with result: {result}", Config.IP);
                CurrentUserId = default!;
                LastArrival = string.Empty;
                CurrentUserName = string.Empty;
            }

            // Check if the day changed from before 5AM to 5AM
            var timeBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TimeAddress, TimeBlock.SIZE, token)
                                            .ConfigureAwait(false);
            var newTimeState = timeBytes.ToClass<TimeBlock>();

            // If we rolled over from before 5AM to 5AM, mark the game dirty
            if (LastTimeState.Hour < 5 && newTimeState.Hour == 5)
                GameIsDirty = true;
            LastTimeState = newTimeState;

            // Update charge percent
            ChargePercent = await SwitchConnection.GetChargePercentAsync(token).ConfigureAwait(false);

            await Task.Delay(1_000, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes a single order from the queue, going through either a fresh game restart
        /// or mid-session approach depending on <see cref="GameIsDirty"/>.
        /// </summary>
        private async Task<OrderResult> ExecuteOrder(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {order.OrderID})" : string.Empty;
            string startMsg = $"Starting order for: {order.VillagerName}{idToken}. " +
                              $"Q Size: {Orders.ToArray().Length + 1}.";
            LogUtil.LogInfo($"{startMsg} ({order.UserGuid})", Config.IP);

            if (!string.IsNullOrEmpty(order.VillagerName) &&
                Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
            {
                await AttemptEchoHook($"> {startMsg}", Config.OrderConfig.EchoArrivingLeavingChannels, token)
                    .ConfigureAwait(false);
            }

            CurrentUserName = order.VillagerName;

            // Clear leftover injections
            Injections.ClearQueue();
            Speaks.ClearQueue();

            // 6 minutes more than user time allowed as a buffer
            int timeOut = (Config.OrderConfig.UserTimeAllowed + 360) * 1_000;
            using var cts = new CancellationTokenSource(timeOut);

            OrderResult result = OrderResult.Faulted;
            var orderTask = GameIsDirty
                ? ExecuteOrderStart(order, false, true, cts.Token)
                : ExecuteOrderMidway(order, cts.Token);

            try
            {
                result = await orderTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException e)
            {
                LogUtil.LogInfo(
                    $"{order.VillagerName} ({order.UserGuid}) timed out: {e.Message}",
                    Config.IP
                );
                order.OrderCancelled(this, "Unfortunately a game crash occurred during your order. Your request is removed.", true);
            }

            if (result == OrderResult.Success)
            {
                // Attempt to close the gate after a successful order
                GameIsDirty = await CloseGate(token).ConfigureAwait(false);
            }
            else
            {
                // If order failed, end the session
                await EndSession(token).ConfigureAwait(false);
                GameIsDirty = true;
            }

            // Clear last arrival name in memory
            await Connection.WriteBytesAsync(new byte[0x14], (uint)OffsetHelper.ArriverNameLocAddress, token)
                            .ConfigureAwait(false);

            return result;
        }

        /// <summary>
        /// Executes an order mid-session (i.e., the game is not restarted).
        /// Clears the map, injects the requested items, and obtains a Dodo code.
        /// </summary>
        private async Task<OrderResult> ExecuteOrderMidway(IACNHOrderNotifier<Item> order, CancellationToken token)
        {
            // Wait for overworld
            while ((await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                      .ConfigureAwait(false)) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            order.OrderInitializing(this, string.Empty);

            // Clear map & spawn
            await ClearMapAndSpawnInternally(order.Order, Map, includeAdditionalParams: false, token).ConfigureAwait(false);

            // Inject the order itself
            await InjectOrder(Map, token).ConfigureAwait(false);

            // If a villager is requested, inject it too
            if (order.VillagerOrder != null)
                await Villagers.InjectVillager(order.VillagerOrder, token).ConfigureAwait(false);

            // Teleport to Orville anchor
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            // Fetch dodo code & wait for user arrival
            return await FetchDodoAndAwaitOrder(order, ignoreInjection: false, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Fully restarts the game session (closes software, re-launches) if needed,
        /// then injects the order and obtains a Dodo code from scratch.
        /// </summary>
        private async Task<OrderResult> ExecuteOrderStart(
            IACNHOrderNotifier<Item> order,
            bool ignoreInjection,
            bool fromRestart,
            CancellationToken token)
        {
            // Detach the controller first
            await DetachControllerAsync(token);

            // If fromRestart is true, we fully close & reopen the game
            if (fromRestart)
            {
                await HandleGameRestartAsync(order, ignoreInjection, token);
            }

            // Wait until we are in the overworld
            await WaitForOverworldStateAsync(token, ignoreInjection);

            // If holding any item, unhold it (press DDOWN)
            await HandleItemUnholdAsync(token);

            LogUtil.LogInfo("Reached overworld, teleporting to the airport.", Config.IP);

            // Move to airport anchor
            await InjectAirportEntryAnchorAsync(token, ignoreInjection);

            // Enter the airport & handle Orville logic
            await EnterAirportAndProceedAsync(order, ignoreInjection, token);

            // Finally, fetch a Dodo code and wait for the user to arrive
            return await FetchDodoAndAwaitOrder(order, ignoreInjection, token).ConfigureAwait(false);
        }

        #endregion

        #region Restart & Airport Logic

        /// <summary>Detaches the virtual controller, effectively resetting input state.</summary>
        private async Task DetachControllerAsync(CancellationToken token)
        {
            await Connection.SendAsync(SwitchCommand.DetachController(), token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Closes the game from the Home menu, restarts it, and handles any "system update" prompts if configured.
        /// </summary>
        private async Task RestartGame(CancellationToken token)
        {
            // Press B to close any dialogs, then HOME
            await Click(SwitchButton.B, 500, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);

            await Click(SwitchButton.HOME, 800, token).ConfigureAwait(false);
            await Task.Delay(300, token).ConfigureAwait(false);

            // Press X to close software, then A to confirm
            await Click(SwitchButton.X, 500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 500, token).ConfigureAwait(false);

            // Wait for "closing software" spinner
            await Task.Delay(3_500 + Config.RestartGameWait, token).ConfigureAwait(false);

            // Press A to launch game again
            await Click(SwitchButton.A, 1_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            // If user wants to avoid system updates, press D-UP
            if (Config.AvoidSystemUpdate)
                await Click(SwitchButton.DUP, 600, token).ConfigureAwait(false);

            // Press A a couple times to proceed
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.A, 1_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            // Wait for "checking if the game can be played"
            await Task.Delay(5_000 + Config.RestartGameWait, token).ConfigureAwait(false);

            // Press A a few times for the title screen
            for (int i = 0; i < 3; i++)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Handles a full game restart if needed, then loads the user’s order into memory (if not ignoring injection).
        /// </summary>
        private async Task HandleGameRestartAsync(IACNHOrderNotifier<Item> order, bool ignoreInjection, CancellationToken token)
        {
            await RestartGame(token).ConfigureAwait(false);
            await ResetControllerStickAsync(token).ConfigureAwait(false);

            if (!ignoreInjection)
            {
                await ClearMapAndSpawnOrderAsync(order, token);

                if (order.VillagerOrder != null)
                    await Villagers.InjectVillager(order.VillagerOrder, token).ConfigureAwait(false);
            }

            // Press A on title screen
            await PressTitleScreenButtonAsync(token);
            await WaitForOverworldAfterRestartAsync(token, ignoreInjection, order);
        }

        /// <summary>
        /// Resets the left stick to 0,0 to avoid drifting on load.
        /// </summary>
        private async Task ResetControllerStickAsync(CancellationToken token)
            => await SetStick(SwitchStick.LEFT, 0, 0, 500, token).ConfigureAwait(false);

        /// <summary>
        /// Clears the map with the user’s order items (if any).
        /// </summary>
        private async Task ClearMapAndSpawnOrderAsync(IACNHOrderNotifier<Item> order, CancellationToken token)
            => await ClearMapAndSpawnInternally(order.Order, Map, false, token).ConfigureAwait(false);

        /// <summary>
        /// Presses A on the game’s title screen after re-launching.
        /// </summary>
        private async Task PressTitleScreenButtonAsync(CancellationToken token)
            => await Click(SwitchButton.A, 500, token).ConfigureAwait(false);

        /// <summary>
        /// Waits up to 150 seconds for anchor 0 to match, while occasionally pressing A/B to skip cutscenes.
        /// </summary>
        private async Task WaitForOverworldAfterRestartAsync(
            CancellationToken token,
            bool ignoreInjection,
            IACNHOrderNotifier<Item> order)
        {
            int echoCount = 0;
            bool gameStarted = await EnsureAnchorMatches(
                0,
                150_000,
                async () =>
                {
                    await ClickConversation(SwitchButton.A, 300, token).ConfigureAwait(false);
                    await ClickConversation(SwitchButton.B, 300, token).ConfigureAwait(false);

                    var currentState = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                                         .ConfigureAwait(false);
                    if (echoCount < 5 && currentState == OverworldState.Overworld)
                    {
                        LogUtil.LogInfo("Reached overworld, waiting for anchor 0 to match...", Config.IP);
                        echoCount++;
                    }
                },
                token
            ).ConfigureAwait(false);

            if (!gameStarted)
            {
                string error = "Failed to reach the overworld.";
                LogUtil.LogError($"{error} Trying next request.", Config.IP);
                order.OrderCancelled(this, $"{error} Sorry, your request has been removed.", true);
                throw new OperationCanceledException(error);
            }

            LogUtil.LogInfo("Anchor 0 matched successfully.", Config.IP);

            // If not ignoring injection, actually inject the items + mark order as "initializing"
            if (!ignoreInjection)
            {
                await InjectOrder(Map, token).ConfigureAwait(false);
                order.OrderInitializing(this, string.Empty);
            }
        }

        /// <summary>
        /// Waits for OverworldState.Overworld. If ignoring injection, optionally spam B to skip text.
        /// </summary>
        private async Task WaitForOverworldStateAsync(CancellationToken token, bool ignoreInjection)
        {
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false)
                   != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (ignoreInjection)
                    await Click(SwitchButton.B, 500, token).ConfigureAwait(false);
            }
            // Additional wait for animation to finish
            await Task.Delay(1_800, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Unholds any item if the user is currently holding something in hand (presses DDOWN).
        /// </summary>
        private async Task HandleItemUnholdAsync(CancellationToken token)
            => await Click(SwitchButton.DDOWN, 300, token).ConfigureAwait(false);

        /// <summary>
        /// Sends anchor #2 (airport entry) and optionally handles morning announcements if ignoring injection.
        /// </summary>
        private async Task InjectAirportEntryAnchorAsync(CancellationToken token, bool ignoreInjection)
        {
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            if (ignoreInjection)
            {
                // Possibly handle morning announcements here
                await HandleMorningAnnouncementAsync(token);
            }
        }

        /// <summary>
        /// Simulates repeated button presses to skip the morning announcement text. 
        /// For demonstration, it waits until OverworldState.Overworld is reached.
        /// </summary>
        private async Task HandleMorningAnnouncementAsync(CancellationToken token)
        {
            LogUtil.LogInfo("Starting morning announcement handling.", Config.IP);

            // Perform initial B clicks
            for (int i = 0; i < 3; i++)
            {
                LogUtil.LogInfo($"Performing click {i + 1} of 3 to skip announcement...", Config.IP);
                await Click(SwitchButton.B, 400, token).ConfigureAwait(false);
            }

            // Keep retrying until Overworld
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false)
                   != OverworldState.Overworld)
            {
                LogUtil.LogInfo("Retrying to reach OverworldState.Overworld...", Config.IP);
                await Click(SwitchButton.B, 300, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            // Optional custom logic to confirm announcement completion
            while (!await CheckAnnouncementCompletionAsync(token).ConfigureAwait(false))
            {
                LogUtil.LogInfo("Announcement not yet completed. Retrying...", Config.IP);
                await Click(SwitchButton.B, 300, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            LogUtil.LogInfo("Morning announcement completed successfully.", Config.IP);
        }

        /// <summary>
        /// Placeholder to check if the morning announcement is done. 
        /// Currently just checks if OverworldState == Overworld.
        /// </summary>
        private async Task<bool> CheckAnnouncementCompletionAsync(CancellationToken token)
        {
            var currentState = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                                 .ConfigureAwait(false);
            // If we are in Overworld, we assume the announcement is done
            return currentState == OverworldState.Overworld;
        }

        /// <summary>
        /// Enters the airport, then teleports to Orville (anchor #3).
        /// </summary>
        private async Task EnterAirportAndProceedAsync(IACNHOrderNotifier<Item> order, bool ignoreInjection, CancellationToken token)
        {
            bool atAirport = await EnsureAnchorMatches(
                2,
                10_000,
                async () =>
                {
                    await Click(SwitchButton.A, 300, token).ConfigureAwait(false);
                    await Click(SwitchButton.B, 300, token).ConfigureAwait(false);
                    await SendAnchorBytes(2, token).ConfigureAwait(false);
                },
                token
            ).ConfigureAwait(false);

            await Task.Delay(500, token).ConfigureAwait(false);

            LogUtil.LogInfo("Entering airport.", Config.IP);
            await EnterAirport(token).ConfigureAwait(false);

            var currentState = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                                 .ConfigureAwait(false);
            if (currentState == OverworldState.Null)
                throw new InvalidOperationException("We are in the water—faulted state.");

            // Warp to Orville anchor
            await TeleportToDodoCounterAsync(token);
        }

        /// <summary>
        /// Teleports the player to anchor #3 (Dodo counter), ensuring we end up at the correct spot.
        /// </summary>
        private async Task TeleportToDodoCounterAsync(CancellationToken token)
        {
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);

            int numChecks = 10;
            LogUtil.LogInfo("Attempting to warp to dodo counter...", Config.IP);

            while (!AnchorHelper.DoAnchorsMatch(
                await ReadAnchor(token).ConfigureAwait(false),
                Anchors.Anchors[3]))
            {
                await SendAnchorBytes(3, token).ConfigureAwait(false);
                if (numChecks-- < 0)
                    throw new InvalidOperationException("Failed to warp to dodo counter (faulted state).");

                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Map & Item Spawning

        /// <summary>
        /// Clears the map (if order is provided) and writes the new items. Optionally writes terrain params.
        /// </summary>
        private async Task ClearMapAndSpawnInternally(
            Item[]? order,
            MapTerrainLite clearMap,
            bool includeAdditionalParams,
            CancellationToken token,
            bool forceFullWrite = false)
        {
            if (order != null)
            {
                // Clear existing items at the spawn area
                clearMap.Spawn(MultiItem.DeepDuplicateItem(Item.NO_ITEM, 40));
                // Place the new order
                clearMap.Spawn(order);
            }

            await Task.Delay(2_000, token).ConfigureAwait(false);
            if (order != null)
                LogUtil.LogInfo("Map clear has started.", Config.IP);

            if (forceFullWrite)
            {
                // Write the entire map in one go
                await Connection.WriteBytesAsync(clearMap.StartupBytes, (uint)OffsetHelper.FieldItemStart, token)
                                .ConfigureAwait(false);
            }
            else
            {
                // Read the map first, then only write differences
                var currentMapData = await Connection.ReadBytesAsync(
                    (uint)OffsetHelper.FieldItemStart,
                    MapTerrainLite.ByteSize,
                    token
                ).ConfigureAwait(false);

                var offData = clearMap.GetDifferencePrioritizeStartup(
                    currentMapData,
                    Config.MapPullChunkSize,
                    Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Config.AllowDrop,
                    (uint)OffsetHelper.FieldItemStart
                );

                for (int i = 0; i < offData.Length; i++)
                    await Connection.WriteBytesAsync(offData[i].ToSend, offData[i].Offset, token).ConfigureAwait(false);
            }

            if (includeAdditionalParams)
            {
                await Connection.WriteBytesAsync(clearMap.StartupTerrain, (uint)OffsetHelper.LandMakingMapStart, token)
                                .ConfigureAwait(false);
                await Connection.WriteBytesAsync(clearMap.StartupAcreParams, (uint)OffsetHelper.OutsideFieldStart, token)
                                .ConfigureAwait(false);
            }

            if (order != null)
                LogUtil.LogInfo("Map clear has ended.", Config.IP);
        }

        /// <summary>
        /// Injects the updated map chunks into memory.
        /// </summary>
        private async Task InjectOrder(MapTerrainLite updatedMap, CancellationToken token)
        {
            var mapChunks = updatedMap.GenerateReturnBytes(Config.MapPullChunkSize, (uint)OffsetHelper.FieldItemStart);
            for (int i = 0; i < mapChunks.Length; i++)
            {
                await Connection.WriteBytesAsync(mapChunks[i].ToSend, mapChunks[i].Offset, token)
                                .ConfigureAwait(false);
            }
        }

        #endregion

        #region Fetching Dodo & Arrival Logic

        /// <summary>
        /// Interacts with Orville to fetch a Dodo code, then waits for the visitor to arrive. 
        /// If the code is invalid or the visitor never arrives, the order is canceled.
        /// </summary>
        private async Task<OrderResult> FetchDodoAndAwaitOrder(
            IACNHOrderNotifier<Item> order,
            bool ignoreInjection,
            CancellationToken token)
        {
            LogUtil.LogInfo($"Talking to Orville. Attempting to get Dodo code for {TownName}.", Config.IP);

            if (ignoreInjection)
                await SetScreenCheck(true, token).ConfigureAwait(false);

            await DodoPosition.GetDodoCode((uint)OffsetHelper.DodoAddress, false, token).ConfigureAwait(false);

            // If config says retry on fail, try again
            if (Config.OrderConfig.RetryFetchDodoOnFail && !DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                LogUtil.LogInfo($"No valid Dodo code for {TownName}. Trying again...", Config.IP);
                for (int i = 0; i < 10; i++)
                    await ClickConversation(SwitchButton.B, 600, token).ConfigureAwait(false);

                await DodoPosition.GetDodoCode((uint)OffsetHelper.DodoAddress, true, token).ConfigureAwait(false);
            }

            await SetScreenCheck(false, token).ConfigureAwait(false);

            // If still invalid, cancel
            if (!DodoPosition.IsDodoValid(DodoPosition.DodoCode))
            {
                var error = "Failed to connect to the internet and obtain a Dodo code.";
                LogUtil.LogError($"{error} Next request...", Config.IP);
                order.OrderCancelled(this, $"A connection error occurred: {error} Your request is removed.", true);
                return OrderResult.Faulted;
            }

            // We have a valid code
            DodoCode = DodoPosition.DodoCode;
            LastDodoFetchTime = DateTime.Now;

            // If this is a real order, notify the user
            if (!ignoreInjection)
            {
                order.OrderReady(this,
                    $"You have {(int)(Config.OrderConfig.WaitForArriverTime * 0.9f)} seconds to arrive. Island name is **{TownName}**.",
                    DodoCode);
            }

            if (DodoImageDrawer != null)
                DodoImageDrawer.Draw(DodoCode);

            // Teleport to airport leave zone
            await SendAnchorBytes(4, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await SendAnchorBytes(4, token).ConfigureAwait(false);

            // Walk out
            await Task.Delay(500, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, -20_000, 1_500, token).ConfigureAwait(false);
            await Task.Delay(1_000, token).ConfigureAwait(false);
            await SetStick(SwitchStick.LEFT, 0, 0, 1_500, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                     .ConfigureAwait(false) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            // Wait for animation
            await Task.Delay(1_200, token).ConfigureAwait(false);

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                     .ConfigureAwait(false) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }

            // Teleport to drop zone
            await SendAnchorBytes(1, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await SendAnchorBytes(1, token).ConfigureAwait(false);

            // If ignoring injection, we're done
            if (ignoreInjection)
                return OrderResult.Success;

            LogUtil.LogInfo("Waiting for arrival.", Config.IP);
            var startTime = DateTime.Now;

            // Wait for the new arrival
            while (!await IsArriverNew(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);

                if ((DateTime.Now - startTime).TotalSeconds > Config.OrderConfig.WaitForArriverTime)
                {
                    var error = "Visitor failed to arrive.";
                    LogUtil.LogError($"{error} Removed from queue, moving to next order.", Config.IP);
                    order.OrderCancelled(this, $"{error} Your request was removed.", false);
                    return OrderResult.NoArrival;
                }
            }

            // Log arrival
            var nid = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNID, 8, token).ConfigureAwait(false);
            var islandId = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageId, 4, token)
                                           .ConfigureAwait(false);

            try
            {
                var newnid = BitConverter.ToUInt64(nid, 0);
                var newnislid = BitConverter.ToUInt32(islandId, 0);
                string plaintext = $"Name/ID: {order.VillagerName}-{order.UserGuid}, " +
                                   $"Villager: {LastArrival}-{LastArrivalIsland}";
                LogUtil.LogInfo($"Arrival logged: NID={newnid}, TownID={newnislid}, details={plaintext}", Config.IP);
            }
            catch (Exception e)
            {
                LogUtil.LogInfo(e.Message + "\r\n" + e.StackTrace, Config.IP);
            }

            // Known abuser check
            if (!Config.AllowKnownAbusers)
            {
                LogUtil.LogInfo($"{LastArrival} from {LastArrivalIsland} is a known abuser. Next order...", Config.IP);
                order.OrderCancelled(this, "You are a known abuser. You cannot use this bot.", false);
                return OrderResult.NoArrival;
            }
            else
            {
                LogUtil.LogInfo($"{LastArrival} from {LastArrivalIsland} is a known abuser, but allowed by config.", Config.IP);
            }

            order.SendNotification(this, $"Visitor arriving: {LastArrival}. Your items will be in front once you land.");
            if (!string.IsNullOrEmpty(order.VillagerName) &&
                Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
            {
                await AttemptEchoHook($"> Visitor arriving: {order.VillagerName}",
                    Config.OrderConfig.EchoArrivingLeavingChannels, token).ConfigureAwait(false);
            }

            // Wait 10s for arrival animation
            await Task.Delay(10_000, token).ConfigureAwait(false);

            // Now wait for user to do their item pickups, eventually leaving
            OverworldState state = OverworldState.Unknown;
            bool isUserArriveLeaving = false;

            // Wait until we're back to Overworld
            while (state != OverworldState.Overworld)
            {
                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                          .ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 500, token).ConfigureAwait(false);

                if (!isUserArriveLeaving && state == OverworldState.UserArriveLeaving)
                {
                    // If user is arriving/leaving, show blocker
                    isUserArriveLeaving = true;
                }
                else if (isUserArriveLeaving && state != OverworldState.UserArriveLeaving)
                {
                    isUserArriveLeaving = false;
                }

                await VisitorList.UpdateNames(token).ConfigureAwait(false);
                if (VisitorList.VisitorCount < 2) // Host alone?
                    break;
            }

            // Let the new user use drop commands
            CurrentUserId = order.UserGuid;

            // We give them up to UserTimeAllowed seconds to do their business & leave
            startTime = DateTime.Now;
            bool warned = false;

            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                     .ConfigureAwait(false) != OverworldState.UserArriveLeaving)
            {
                await DropLoop(token).ConfigureAwait(false);
                await Click(SwitchButton.B, 300, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                double elapsed = (DateTime.Now - startTime).TotalSeconds;
                if (elapsed > (Config.OrderConfig.UserTimeAllowed - 60) && !warned)
                {
                    order.SendNotification(
                        this,
                        "You have 60 seconds remaining before I start the next order. " +
                        "Please collect your items and leave."
                    );
                    warned = true;
                }

                if (elapsed > Config.OrderConfig.UserTimeAllowed)
                {
                    var error = "Visitor failed to leave.";
                    LogUtil.LogError($"{error}. Removed from queue, next order.", Config.IP);
                    order.OrderCancelled(this, $"{error} Your request is removed.", false);
                    return OrderResult.NoLeave;
                }

                // If the network session crashed, we fail
                if (!await IsNetworkSessionActive(token).ConfigureAwait(false))
                {
                    var error = "Network crash detected.";
                    LogUtil.LogError($"{error}. Next order...", Config.IP);
                    order.OrderCancelled(this, $"{error} Your request is removed.", true);
                    return OrderResult.Faulted;
                }
            }

            // Arrival -> departure cycle complete
            LogUtil.LogInfo("Order completed. Notifying visitor of completion.", Config.IP);
           
            order.OrderFinished(this, Config.OrderConfig.CompleteOrderMessage);

            if (!string.IsNullOrEmpty(order.VillagerName) &&
                Config.OrderConfig.EchoArrivingLeavingChannels.Count > 0)
            {
                await AttemptEchoHook(
                    $"> Visitor completed order, now leaving: {order.VillagerName}",
                    Config.OrderConfig.EchoArrivingLeavingChannels,
                    token
                ).ConfigureAwait(false);
            }

            await Task.Delay(5_000, token).ConfigureAwait(false);
            await Task.Delay(15_000, token).ConfigureAwait(false);

            // Wait until Overworld again
            while (await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token)
                                     .ConfigureAwait(false) != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                await Click(SwitchButton.B, 300, token).ConfigureAwait(false);
            }

            // Wait final animation
            await Task.Delay(1_200, token).ConfigureAwait(false);
            return OrderResult.Success;
        }

        #endregion

        #region UI & Session End

        /// <summary>
        /// Attempts to close the airport gate after an order is done, returning true if the session remains active.
        /// </summary>
        private async Task<bool> CloseGate(CancellationToken token)
        {
            // Teleport to airport entry anchor (twice)
            await SendAnchorBytes(2, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await SendAnchorBytes(2, token).ConfigureAwait(false);

            // Enter airport
            await EnterAirport(token).ConfigureAwait(false);

            // Teleport to Orville
            await SendAnchorBytes(3, token).ConfigureAwait(false);
            await Task.Delay(500, token).ConfigureAwait(false);
            await SendAnchorBytes(3, token).ConfigureAwait(false);

            // Close gate
            await DodoPosition.CloseGate((uint)OffsetHelper.DodoAddress, token).ConfigureAwait(false);
            await Task.Delay(2_000, token).ConfigureAwait(false);

            return await IsNetworkSessionActive(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Ends the current session by pressing B multiple times, then pressing MINUS, then pressing A to confirm.
        /// Waits 14s for that closing animation to finish.
        /// </summary>
        private async Task EndSession(CancellationToken token)
        {
            for (int i = 0; i < 5; i++)
                await Click(SwitchButton.B, 300, token).ConfigureAwait(false);

            await Task.Delay(500, token).ConfigureAwait(false);
            await Click(SwitchButton.MINUS, 500, token).ConfigureAwait(false);

            for (int i = 0; i < 5; i++)
                await Click(SwitchButton.A, 1_000, token).ConfigureAwait(false);

            await Task.Delay(14_000, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Actually moves the player into the airport by walking forward, pausing any active freeze states while doing so.
        /// </summary>
        private async Task EnterAirport(CancellationToken token)
        {
            // Temporarily pause freeze while loading
            await SwitchConnection.SetFreezePauseState(true, token).ConfigureAwait(false);
            await Task.Delay(200 + Config.ExtraTimeEnterAirportWait, token).ConfigureAwait(false);

            int tries = 0;
            var state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            var baseState = state;

            // Keep walking forward until state changes
            while (baseState == state)
            {
                LogUtil.LogInfo($"Attempting to enter airport. Try: {tries + 1}", Config.IP);
                await SetStick(SwitchStick.LEFT, 20_000, 20_000, 400, token).ConfigureAwait(false);
                await Task.Delay(500, token).ConfigureAwait(false);

                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);

                await SetStick(SwitchStick.LEFT, 0, 0, 600, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);

                tries++;
                if (tries > 6)
                    break;
            }

            // Wait until Overworld
            tries = 0;
            while (state != OverworldState.Overworld)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                state = await DodoPosition.GetOverworldState(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
                tries++;
                if (tries > 12)
                    break;
            }

            // Wait for final animation
            await Task.Delay(1_500, token).ConfigureAwait(false);
            await SwitchConnection.SetFreezePauseState(false, token).ConfigureAwait(false);
        }

        #endregion

        #region Queue & Drop Logic

        /// <summary>
        /// Periodically checks for speak or item injection requests, executes them, and optionally cleans leftover items.
        /// </summary>
        private async Task DropLoop(CancellationToken token)
        {
            if (!Config.AcceptingCommands)
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                return;
            }

            // Speaks have highest priority
            if (Speaks.TryDequeue(out var chat))
            {
                LogUtil.LogInfo($"Now speaking: {chat.User}: {chat.Item}", Config.IP);
                await Speak(chat.Item, token).ConfigureAwait(false);
            }

            // Turnip stonk changes
            else if (StonkRequests.TryDequeue(out var stonk))
            {
                await UpdateTurnips(stonk.Item, token).ConfigureAwait(false);
                stonk.OnFinish?.Invoke(true);
            }

            // Item injection requests
            else if (Injections.TryDequeue(out var item))
            {
                int count = await DropItems(item, token).ConfigureAwait(false);
                State.AfterDrop(count);
            }
            else if ((State.CleanRequired && State.Config.AutoClean) || CleanRequested)
            {
                await CleanUp(State.Config.PickupCount, token).ConfigureAwait(false);
                State.AfterClean();
                CleanRequested = false;
            }
            else
            {
                State.StillIdle();
                await Task.Delay(300, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Inserts text into the chat buffer (UTF-16), then “speaks” it in-game via the PLUS button.
        /// </summary>
        private async Task Speak(string toSpeak, CancellationToken token)
        {
            ChatAddress = await DodoPosition.FollowMainPointer(OffsetHelper.ChatCoordJumps, token).ConfigureAwait(false);
            await Task.Delay(200, token).ConfigureAwait(false);

            await Click(SwitchButton.R, 500, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 400, token).ConfigureAwait(false);
            await Click(SwitchButton.A, 400, token).ConfigureAwait(false);

            // Write the chat text as UTF-16
            var chatBytes = Encoding.Unicode.GetBytes(toSpeak);
            var sendBytes = new byte[OffsetHelper.ChatBufferSize * 2];
            Array.Copy(chatBytes, sendBytes, chatBytes.Length);

            await SwitchConnection.WriteBytesAbsoluteAsync(sendBytes, ChatAddress, token).ConfigureAwait(false);

            await Click(SwitchButton.PLUS, 200, token).ConfigureAwait(false);

            // Exit out of any menus
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.B, 400, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Updates all turnip selling prices to a specified new value.
        /// </summary>
        private async Task UpdateTurnips(int newStonk, CancellationToken token)
        {
            var stonkBytes = await Connection.ReadBytesAsync((uint)OffsetHelper.TurnipAddress, TurnipStonk.SIZE, token)
                                             .ConfigureAwait(false);
            var newStonkBytes = BitConverter.GetBytes(newStonk);

            // Overwrite each day’s value
            for (int i = 0; i < 12; i++)
                Array.Copy(newStonkBytes, 0, stonkBytes, 12 + (i * 4), newStonkBytes.Length);

            await Connection.WriteBytesAsync(stonkBytes, (uint)OffsetHelper.TurnipAddress, token)
                            .ConfigureAwait(false);
        }

        /// <summary>
        /// Validates whether the inventory offset is actually a valid player inventory block.
        /// </summary>
        private async Task<bool> GetIsPlayerInventoryValid(uint playerOfs, CancellationToken token)
        {
            var (ofs, len) = InventoryValidator.GetOffsetLength(playerOfs);
            var inventory = await Connection.ReadBytesAsync(ofs, len, token).ConfigureAwait(false);
            return InventoryValidator.ValidateItemBinary(inventory);
        }

        /// <summary>
        /// Drops each item in the request onto the ground by injecting into inventory and selecting "drop".
        /// </summary>
        private async Task<int> DropItems(ItemRequest drop, CancellationToken token)
        {
            int dropped = 0;
            bool first = true;
            foreach (var item in drop.Item)
            {
                await DropItem(item, first, token).ConfigureAwait(false);
                first = false;
                dropped++;
            }
            return dropped;
        }

        /// <summary>
        /// Injects a single item into the player's inventory, then performs the "drop item" 
        /// action in-game, optionally restoring the original inventory.
        /// </summary>
        private async Task DropItem(Item item, bool first, CancellationToken token)
        {
            // Close any open menus if it's the first item
            if (first)
            {
                for (int i = 0; i < 3; i++)
                    await Click(SwitchButton.B, 400, token).ConfigureAwait(false);
            }

            var itemName = GameInfo.Strings.GetItemName(item);
            LogUtil.LogInfo($"Injecting Item: 0x{item.DisplayItemId:X4} ({itemName}).", Config.IP);

            Item[]? startItems = null;

            // If not using legacy drop, read + inject droppable placeholder first
            if (!Config.DropConfig.UseLegacyDrop)
            {
                (InjectionResult result, Item[]? readItems) = await PocketInjector.Read(token).ConfigureAwait(false);
                if (result != InjectionResult.Success)
                    LogUtil.LogInfo($"Inventory read failed: {result}", Config.IP);

                startItems = readItems;
                await PocketInjector.Write40(PocketInjector.DroppableOnlyItem, token);
                await Task.Delay(300, token).ConfigureAwait(false);

                // Open inventory, press A
                await Click(SwitchButton.X, 1_200, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 500, token).ConfigureAwait(false);

                // Inject the correct item
                await PocketInjector.Write40(item, token);
                await Task.Delay(300, token).ConfigureAwait(false);
            }
            else
            {
                // Legacy approach
                byte[] data = item.ToBytesClass();
                var poke = SwitchCommand.Poke(InventoryOffset, data);
                await Connection.SendAsync(poke, token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);

                // Open inventory & press A
                await Click(SwitchButton.X, 1_100, token).ConfigureAwait(false);
                await Click(SwitchButton.A, 500, token).ConfigureAwait(false);

                // Navigate down to the "drop item" option
                int downCount = item.GetItemDropOption();
                for (int i = 0; i < downCount; i++)
                    await Click(SwitchButton.DDOWN, 400, token).ConfigureAwait(false);
            }

            // Press A to drop, then close menu with X
            await Click(SwitchButton.A, 400, token).ConfigureAwait(false);
            await Click(SwitchButton.X, 400, token).ConfigureAwait(false);

            // Exit out of any leftover menus
            for (int i = 0; i < 2; i++)
                await Click(SwitchButton.B, 400, token).ConfigureAwait(false);

            // Restore original inventory if we read it
            if (startItems != null)
                await PocketInjector.Write(startItems, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Picks up leftover items (pressing Y repeatedly), injecting a "NONE" item to effectively "delete" them.
        /// </summary>
        private async Task CleanUp(int count, CancellationToken token)
        {
            LogUtil.LogInfo("Picking up leftover items during idle time.", Config.IP);

            // Close menus
            for (int i = 0; i < 3; i++)
                await Click(SwitchButton.B, 400, token).ConfigureAwait(false);

            // Poke a "None" item into the inventory offset
            var poke = SwitchCommand.Poke(InventoryOffset, Item.NONE.ToBytes());
            await Connection.SendAsync(poke, token).ConfigureAwait(false);

            // Perform the pickup
            for (int i = 0; i < count; i++)
            {
                await Click(SwitchButton.Y, 2_000, token).ConfigureAwait(false);
                await Connection.SendAsync(poke, token).ConfigureAwait(false);
                await Task.Delay(1_000, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Anchors & Save Files

        /// <summary>
        /// Writes the maximum text speed, then clicks a button with a delay.
        /// </summary>
        public async Task ClickConversation(SwitchButton b, int delay, CancellationToken token)
        {
            await Connection.WriteBytesAsync(MaxTextSpeed, (int)OffsetHelper.TextSpeedAddress, token).ConfigureAwait(false);
            await Click(b, delay, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Puts the console screen into sleep mode or wakes it up, if the config allows.
        /// </summary>
        public async Task SetScreenCheck(bool on, CancellationToken token, bool force = false)
        {
            if (!Config.ExperimentalSleepScreenOnIdle && !force) return;
            await SetScreen(on, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Notifies all registered IDodoRestoreNotifiers of the new dodo code.
        /// </summary>
        private void NotifyDodo(string dodo)
        {
            foreach (var n in DodoNotifiers)
                n.NotifyServerOfDodoCode(dodo);
        }

        /// <summary>
        /// Notifies all IDodoRestoreNotifiers of a state change (Active, Fetching, etc.).
        /// </summary>
        private void NotifyState(GameState st)
        {
            foreach (var n in DodoNotifiers)
                n.NotifyServerOfState(st);
        }

        /// <summary>
        /// Ensures anchors are initialized (i.e. none is empty) unless ForceUpdateAnchors is true.
        /// </summary>
        private async Task EnsureAnchorsAreInitialised(CancellationToken token)
        {
            bool loggedBadAnchors = false;
            while (Config.ForceUpdateAnchors || Anchors.IsOneEmpty(out _))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                if (!loggedBadAnchors)
                {
                    LogUtil.LogInfo("Anchors are not initialised.", Config.IP);
                    loggedBadAnchors = true;
                }
            }
        }

        /// <summary>
        /// Updates a specific anchor (anchorIndex) with the current position/rotation data in memory.
        /// </summary>
        public async Task<bool> UpdateAnchor(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index >= anchors.Length)
                return false;

            var anchor = await ReadAnchor(token).ConfigureAwait(false);
            anchors[index].Anchor1 = anchor.Anchor1;
            anchors[index].Anchor2 = anchor.Anchor2;
            Anchors.Save();

            LogUtil.LogInfo($"Updated anchor {index}.", Config.IP);
            return true;
        }

        /// <summary>
        /// Sends the specified anchor bytes into memory, effectively teleporting the player.
        /// </summary>
        public async Task<bool> SendAnchorBytes(int index, CancellationToken token)
        {
            var anchors = Anchors.Anchors;
            if (index < 0 || index >= anchors.Length)
                return false;

            ulong offset = await DodoPosition.FollowMainPointer(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(anchors[index].Anchor1, offset, token).ConfigureAwait(false);
            await SwitchConnection.WriteBytesAbsoluteAsync(anchors[index].Anchor2, offset + 0x3C, token)
                                  .ConfigureAwait(false);

            return true;
        }

        /// <summary>
        /// Reads the current anchor bytes (pos + rot) from memory.
        /// </summary>
        private async Task<PosRotAnchor> ReadAnchor(CancellationToken token)
        {
            ulong offset = await DodoPosition.FollowMainPointer(OffsetHelper.PlayerCoordJumps, token).ConfigureAwait(false);
            var bytesA = await SwitchConnection.ReadBytesAbsoluteAsync(offset, 0xC, token).ConfigureAwait(false);
            var bytesB = await SwitchConnection.ReadBytesAbsoluteAsync(offset + 0x3C, 0x4, token).ConfigureAwait(false);
            var combined = bytesA.Concat(bytesB).ToArray();
            return new PosRotAnchor(combined);
        }

        /// <summary>
        /// Checks if the anchor in memory matches the anchor stored at anchorIndex.
        /// </summary>
        private async Task<bool> DoesAnchorMatch(int anchorIndex, CancellationToken token)
        {
            var anchorMemory = await ReadAnchor(token).ConfigureAwait(false);
            return anchorMemory.AnchorBytes.SequenceEqual(Anchors.Anchors[anchorIndex].AnchorBytes);
        }

        /// <summary>
        /// Waits up to millisecondsTimeout for the anchor at anchorIndex to match, repeatedly running toDoPerLoop in between checks.
        /// </summary>
        private async Task<bool> EnsureAnchorMatches(int anchorIndex, int millisecondsTimeout, Func<Task> toDoPerLoop, CancellationToken token)
        {
            bool success = false;
            var startTime = DateTime.Now;

            while (!success)
            {
                if (toDoPerLoop != null)
                    await toDoPerLoop().ConfigureAwait(false);

                bool anchorMatches = await DoesAnchorMatch(anchorIndex, token).ConfigureAwait(false);
                if (!anchorMatches)
                    await Task.Delay(500, token).ConfigureAwait(false);
                else
                    success = true;

                if ((DateTime.Now - startTime).TotalMilliseconds > millisecondsTimeout)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Checks memory to see if the current "arriver" name is new (not empty and not the same as last arrival).
        /// </summary>
        private async Task<bool> IsArriverNew(CancellationToken token)
        {
            var data = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverNameLocAddress, 0x14, token)
                                       .ConfigureAwait(false);
            string arriverName = Encoding.Unicode.GetString(data).TrimEnd('\0');
            if (!string.IsNullOrEmpty(arriverName) && arriverName != LastArrival)
            {
                LastArrival = arriverName;

                var islandData = await Connection.ReadBytesAsync((uint)OffsetHelper.ArriverVillageLocAddress, 0x14, token)
                                                 .ConfigureAwait(false);
                LastArrivalIsland = Encoding.Unicode.GetString(islandData).TrimEnd('\0').TrimEnd();

                LogUtil.LogInfo($"{arriverName} from {LastArrivalIsland} is arriving!", Config.IP);

                if (Config.HideArrivalNames)
                {
                    var blank = new byte[0x14];
                    await Connection.WriteBytesAsync(blank, (uint)OffsetHelper.ArriverNameLocAddress, token).ConfigureAwait(false);
                    await Connection.WriteBytesAsync(blank, (uint)OffsetHelper.ArriverVillageLocAddress, token).ConfigureAwait(false);
                }
                return true;
            }
            return false;
        }

        #endregion

        #region Reading & Saving Data

        /// <summary>
        /// Loads an initial .nhl layer if present, updating <see cref="CLayer"/> and the underlying map data.
        /// </summary>
        private async Task LoadInitialLayerAsync(CancellationToken token)
        {
            string layerFileNameNoExt = Config.FieldLayerName;
            string layerFile = Path.Combine(Config.FieldLayerNHLDirectory, layerFileNameNoExt + ".nhl");

            if (File.Exists(layerFile))
            {
                CLayer = layerFileNameNoExt;
                var bytes1 = File.ReadAllBytes(layerFile);
                LogUtil.LogInfo($"Layer {layerFile} loaded.", Config.IP);

                var terrainData = await Connection.ReadBytesAsync((uint)OffsetHelper.LandMakingMapStart, MapTerrainLite.TerrainSize, token).ConfigureAwait(false);
                var mapParams = await Connection.ReadBytesAsync((uint)OffsetHelper.OutsideFieldStart, MapTerrainLite.AcrePlusAdditionalParams, token).ConfigureAwait(false);

                Map = new MapTerrainLite(bytes1, terrainData, mapParams)
                {
                    SpawnX = Config.MapPlaceX,
                    SpawnY = Config.MapPlaceY
                };
            }
        }

        /// <summary>
        /// Reads the original map items & terrain data from the device to set <see cref="Map"/>.
        /// </summary>
        private async Task ReadOriginalMapAsync(CancellationToken token)
        {
            LogUtil.LogInfo("Reading original map status. Please wait...", Config.IP);

            var itemBytes = await Connection.ReadBytesAsync(
                (uint)OffsetHelper.FieldItemStart,
                MapGrid.MapTileCount32x32 * Item.SIZE,
                token
            ).ConfigureAwait(false);

            var terrainBytes = await Connection.ReadBytesAsync(
                (uint)OffsetHelper.LandMakingMapStart,
                MapTerrainLite.TerrainSize,
                token
            ).ConfigureAwait(false);

            var acreParams = await Connection.ReadBytesAsync(
                (uint)OffsetHelper.OutsideFieldStart,
                MapTerrainLite.AcrePlusAdditionalParams,
                token
            ).ConfigureAwait(false);

            Map = new MapTerrainLite(itemBytes, terrainBytes, acreParams)
            {
                SpawnX = Config.MapPlaceX,
                SpawnY = Config.MapPlaceY
            };
        }

        /// <summary>
        /// Finds the player inventory offset or calculates it dynamically, 
        /// e.g., if multiple players are connected. 
        /// If you have a more advanced approach, place it here.
        /// </summary>
        private async Task<uint> GetCurrentPlayerOffset(uint baseOffset, uint playerSize, CancellationToken token)
        {
            // TODO: Implement logic to locate the correct offset of the current player’s inventory.
            // For now, just return the baseOffset to compile.
            await Task.Delay(1, token).ConfigureAwait(false);
            return baseOffset;
        }

        /// <summary>
        /// Sends a message to the specified Discord channels (given their IDs),
        /// possibly avoiding double-posts if checkForDoublePosts is true.
        /// </summary>
        private async Task AttemptEchoHook(
            string message,
            IReadOnlyCollection<ulong> channels,
            CancellationToken token,
            bool checkForDoublePosts = false)
        {
            foreach (var msgChannel in channels)
            {
                // 'Globals.Self' is presumably your SysCord object 
                // with a .TrySpeakMessage(...) method to speak to Discord.
                bool success = await Globals.Self.TrySpeakMessage(msgChannel, message, checkForDoublePosts).ConfigureAwait(false);
                if (!success)
                    LogUtil.LogError($"Unable to post into channel ID {msgChannel}.", Config.IP);
            }

            // Possibly also log the echo to a file or console
            LogUtil.LogText($"Echo: {message}");
        }



        /// <summary>
        /// Saves the current list of villagers to the configured file.
        /// </summary>
        private async Task SaveVillagersToFile(CancellationToken token)
        {
            string text = Config.DodoModeConfig.MinimizeDetails
                ? Villagers.LastVillagers
                : $"Villagers on {TownName}: {Villagers.LastVillagers}";

            var data = Encoding.ASCII.GetBytes(text);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.VillagerFilename, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Saves the current dodo code (or "TownName: DodoCode") to the configured file.
        /// </summary>
        private async Task SaveDodoCodeToFile(CancellationToken token)
        {
            string text = Config.DodoModeConfig.MinimizeDetails
                ? DodoCode
                : $"{TownName}: {DodoCode}";

            var data = Encoding.ASCII.GetBytes(text);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.DodoRestoreFilename, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Saves the name of the currently loaded layer (.nhl) without extension.
        /// </summary>
        private async Task SaveLayerNameToFile(string name, CancellationToken token)
        {
            var data = Encoding.ASCII.GetBytes(name);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.LoadedNHLFilename, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes the current visitor count and list to the respective files (VisitorFilename, VisitorListFilename).
        /// </summary>
        private async Task SaveVisitorsToFile(CancellationToken token)
        {
            string visitorInfo;
            if (VisitorList.VisitorCount == VisitorListHelper.VisitorListSize)
            {
                visitorInfo = Config.DodoModeConfig.MinimizeDetails ? "FULL" : $"{TownName} is full";
            }
            else
            {
                // VisitorCount - 1 for the host
                uint visitorCount = VisitorList.VisitorCount - 1;
                visitorInfo = Config.DodoModeConfig.MinimizeDetails ? $"{visitorCount}" : $"Visitors: {visitorCount}";
            }

            var data = Encoding.ASCII.GetBytes(visitorInfo);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.VisitorFilename, token).ConfigureAwait(false);

            data = Encoding.ASCII.GetBytes(VisitorList.VisitorFormattedString);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.VisitorListFilename, token)
                           .ConfigureAwait(false);
        }

        /// <summary>
        /// Resets the dodo code, visitor count, and visitor list files to "FETCHING" or "0" states.
        /// </summary>
        private async Task ResetFiles(CancellationToken token)
        {
            string text = Config.DodoModeConfig.MinimizeDetails ? "FETCHING" : $"{TownName}: FETCHING";
            DodoCode = text;

            var data = Encoding.ASCII.GetBytes(text);
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.DodoRestoreFilename, token).ConfigureAwait(false);

            data = Encoding.ASCII.GetBytes(Config.DodoModeConfig.MinimizeDetails ? "0" : "Visitors: 0");
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.VisitorFilename, token).ConfigureAwait(false);

            data = Encoding.ASCII.GetBytes(Config.DodoModeConfig.MinimizeDetails ? "No-one" : "No visitors");
            await FileUtil.WriteBytesToFileAsync(data, Config.DodoModeConfig.VisitorListFilename, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Checks if the online session is active (byte at OnlineSessionAddress).
        /// </summary>
        private async Task<bool> IsNetworkSessionActive(CancellationToken token)
        {
            var data = await Connection.ReadBytesAsync((uint)OffsetHelper.OnlineSessionAddress, 1, token).ConfigureAwait(false);
            return data[0] == 1;
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class ExternalMapHelper
    {
        private readonly string _rootPathNHL;
        private readonly Dictionary<string, byte[]> _loadedNHLs;
        private readonly bool _cycleMap;
        private readonly int _cycleTime;
        private DateTime _lastCycleTime;
        private int _lastCycledIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalMapHelper"/> class.
        /// </summary>
        /// <param name="cfg">The configuration object containing settings for the bot.</param>
        /// <param name="lastFileLoaded">The name of the last file that was loaded.</param>
        public ExternalMapHelper(CrossBotConfig cfg, string lastFileLoaded)
        {
            _rootPathNHL = cfg.FieldLayerNHLDirectory;
            _loadedNHLs = new Dictionary<string, byte[]>();

            if (!Directory.Exists(_rootPathNHL))
                Directory.CreateDirectory(_rootPathNHL);

            LoadNHLFiles();

            _cycleMap = cfg.DodoModeConfig.CycleNHLs;
            _cycleTime = cfg.DodoModeConfig.CycleNHLMinutes;
            _lastCycleTime = DateTime.Now;

            _lastCycledIndex = _loadedNHLs.Keys
                .ToList()
                .FindIndex(x => x.Equals(lastFileLoaded, StringComparison.InvariantCultureIgnoreCase));
        }

        /// <summary>
        /// Loads NHL files from the root path into the dictionary.
        /// </summary>
        private void LoadNHLFiles()
        {
            foreach (var file in Directory.EnumerateFiles(_rootPathNHL))
            {
                var info = new FileInfo(file);
                if (info.Length == ACNHMobileSpawner.MapTerrainLite.ByteSize)
                    _loadedNHLs[info.Name] = File.ReadAllBytes(file);
            }
        }

        /// <summary>
        /// Gets the NHL file as a byte array.
        /// </summary>
        /// <param name="filename">The name of the NHL file.</param>
        /// <returns>A byte array representing the NHL file, or null if not found.</returns>
        public byte[]? GetNHL(string filename)
        {
            filename = filename.EndsWith(".nhl", StringComparison.OrdinalIgnoreCase) ? filename : $"{filename}.nhl";
            if (_loadedNHLs.TryGetValue(filename, out var nhlData))
                return nhlData;

            var filePath = Path.Combine(_rootPathNHL, filename);
            return File.Exists(filePath) ? LoadAndCacheNHL(filePath) : null;
        }

        /// <summary>
        /// Loads and caches an NHL file.
        /// </summary>
        /// <param name="filePath">The path to the NHL file.</param>
        /// <returns>A byte array representing the NHL file, or null if the file is invalid.</returns>
        private byte[]? LoadAndCacheNHL(string filePath)
        {
            var bytes = File.ReadAllBytes(filePath);
            if (bytes.Length == ACNHMobileSpawner.MapTerrainLite.ByteSize)
            {
                var filename = Path.GetFileName(filePath);
                _loadedNHLs[filename] = bytes;
                return bytes;
            }
            return null;
        }

        /// <summary>
        /// Checks whether it is time to cycle to the next NHL file.
        /// </summary>
        /// <param name="request">The map override request if cycling occurs.</param>
        /// <returns>True if a cycle occurred, otherwise false.</returns>
        public bool CheckForCycle(out MapOverrideRequest? request)
        {
            request = null;
            if (!_cycleMap || _loadedNHLs.Count == 0)
                return false;

            var now = DateTime.Now;
            bool shouldCycle = _cycleTime == -1 ? _lastCycleTime.Date != now.Date : (now - _lastCycleTime).TotalMinutes >= _cycleTime;
            if (shouldCycle)
            {
                _lastCycleTime = now;
                _lastCycledIndex = (_lastCycledIndex + 1) % _loadedNHLs.Count;
                var nhl = _loadedNHLs.ElementAt(_lastCycledIndex);
                request = new MapOverrideRequest(nameof(ExternalMapHelper), nhl.Value, nhl.Key);
                return true;
            }

            return false;
        }
    }
}

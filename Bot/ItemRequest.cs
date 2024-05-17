using System;
using System.Collections.Generic;
using System.IO;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    // Abstract base class for requests
    public abstract class Request<T>
    {
        public readonly string User;
        public readonly T Item;
        public Action<bool>? OnFinish { get; set; }

        protected Request(string usr, T item)
        {
            User = usr;
            Item = item;
        }
    }

    // Specific request classes

    // ItemRequest: Handles requests for items
    public sealed class ItemRequest : Request<IReadOnlyCollection<Item>>
    {
        public ItemRequest(string user, IReadOnlyCollection<Item> items) : base(user, items) { }
    }

    // VillagerRequest: Handles requests for villagers
    public sealed class VillagerRequest : Request<VillagerData>
    {
        public readonly byte Index;
        public readonly string GameName;

        public VillagerRequest(string user, VillagerData data, byte index, string gameName) : base(user, data)
        {
            Index = index;
            GameName = gameName;
        }
    }

    // SpeakRequest: Handles text requests
    public sealed class SpeakRequest : Request<string>
    {
        public SpeakRequest(string user, string text) : base(user, text) { }
    }

    // MapOverrideRequest: Handles map override requests
    public sealed class MapOverrideRequest : Request<byte[]>
    {
        public readonly string OverrideLayerName;

        public MapOverrideRequest(string user, byte[] fieldLayerBytes, string layerName) : base(user, fieldLayerBytes)
        {
            if (fieldLayerBytes.Length != ACNHMobileSpawner.MapTerrainLite.ByteSize)
                throw new Exception("Attempting to inject mapdata of the incorrect size.");

            OverrideLayerName = layerName;
            Globals.Bot.CLayer = $"{Path.GetFileNameWithoutExtension(layerName)}";
        }
    }

    // TurnipRequest: Handles requests for turnip prices
    public sealed class TurnipRequest : Request<int>
    {
        public TurnipRequest(string user, int stonk) : base(user, stonk) { }
    }
}

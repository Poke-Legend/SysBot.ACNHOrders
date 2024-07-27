using NHSE.Core;
using System;
using System.Collections.Generic;

namespace SysBot.ACNHOrders
{
    public class InternalItemTool
    {
        private readonly List<ushort> InternalItems;
        private static readonly InternalItemTool internalItemTool = new();

        public static InternalItemTool CurrentInstance { get; set; } = internalItemTool;

        private static readonly string[] separator = new string[3] { "\r\n", "\r", "\n" };

        public InternalItemTool()
        {
            var items = FileUtil.GetEmbeddedResource("SysBot.ACNHOrders.Resources", "InternalHexList.txt");
            var spl = items.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            InternalItems = new List<ushort>();
            foreach (var it in spl)
                InternalItems.Add(ushort.Parse(it, System.Globalization.NumberStyles.HexNumber));
        }

        public bool IsInternalItem(ushort val) => InternalItems.Contains(val);

        public bool IsSane(IReadOnlyCollection<Item> items, IConfigItem cfg)
        {
            if (cfg.SkipDropCheck)
                return true;
            foreach (var it in items)
            {
                if (IsInternalItem(it.ItemId))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

using NHSE.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class MultiItem
    {
        public const int MaxOrder = 40;
        public ItemArrayEditor<Item> ItemArray { get; private set; }

        /// <summary>
        /// The only constructor. It has four parameters:
        /// 1) items[] (required)
        /// 2) catalogue (bool, optional, default false)
        /// 3) fillToMax (bool, optional, default true)
        /// 4) stackMax (bool, optional, default true)
        /// 
        /// No parameter named 'isOrder' exists here, so do NOT call with isOrder:.
        /// </summary>
        public MultiItem(Item[] items, bool catalogue = false, bool fillToMax = true, bool stackMax = true)
        {
            var itemArray = items;
            if (stackMax)
                StackToMax(itemArray);

            if (items.Length < MaxOrder && fillToMax && !catalogue)
            {
                var newItems = new List<Item>(items);
                foreach (var currentItem in items)
                {
                    // This logic tries to replicate items to fill up to MaxOrder if needed.
                    int itemMultiplier = (int)(1f / ((1f / MaxOrder) * items.Length));
                    ProcessItem(currentItem, itemMultiplier, newItems);
                    if (newItems.Count >= MaxOrder)
                        break;
                }

                // If we exceeded 40, cut it down to 40
                itemArray = newItems.Count > MaxOrder
                    ? newItems.Take(MaxOrder).ToArray()
                    : newItems.ToArray();
            }

            // Now fill to max if necessary
            var itemsToAdd = (Item[])itemArray.Clone();
            FillToMax(items, catalogue, ref itemsToAdd);

            ItemArray = new ItemArrayEditor<Item>(itemsToAdd);
        }

        /// <summary>
        /// Parameterless constructor, if you need an empty MultiItem.
        /// </summary>
        public MultiItem()
        {
            ItemArray = new ItemArrayEditor<Item>(Array.Empty<Item>());
        }

        private static void ProcessItem(Item currentItem, int itemMultiplier, List<Item> newItems)
        {
            var remake = ItemRemakeUtil.GetRemakeIndex(currentItem.ItemId);
            var associated = GameInfo.Strings.GetAssociatedItems(currentItem.ItemId, out _);
            getMaxStack(currentItem, out var stackedMax);

            if (remake > 0 && currentItem.Count == 0)
            {
                ProcessRemakeItem(currentItem, itemMultiplier, newItems, (short)remake);
            }
            else if (stackedMax < 2 && associated.Count > 1 && currentItem.ItemId != Item.DIYRecipe)
            {
                // If item can have multiple 'associated' forms (like color variations), we add them all
                foreach (var assoc in associated)
                {
                    var toAdd = new Item();
                    toAdd.CopyFrom(currentItem);
                    toAdd.ItemId = (ushort)assoc.Value;
                    newItems.Add(toAdd);
                }
            }
            else if (remake < 0)
            {
                // If not a 'remake' item, just duplicate it
                newItems.AddRange(DeepDuplicateItem(currentItem, Math.Max(0, itemMultiplier - 1)));
            }
        }

        private static void ProcessRemakeItem(Item currentItem, int itemMultiplier, List<Item> newItems, short remake)
        {
            var info = ItemRemakeInfoData.List[remake];
            var body = info.GetBodySummary(GameInfo.Strings);
            var bodyVariations = body.Split(new[] { "\n", "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (bodyVariations.Length < 1)
            {
                newItems.AddRange(DeepDuplicateItem(currentItem, Math.Max(0, itemMultiplier - 1)));
                return;
            }

            int varCount = bodyVariations.Length;
            if (!bodyVariations[0].StartsWith("0"))
                varCount++;

            var multipliedItems = DeepDuplicateItem(currentItem, varCount);
            for (ushort j = 1; j < varCount; ++j)
            {
                var itemToAdd = multipliedItems[j];
                itemToAdd.Count = (ushort)j;
                newItems.Add(itemToAdd);
            }
        }

        private static void FillToMax(Item[] items, bool catalogue, ref Item[] itemsToAdd)
        {
            int len = itemsToAdd.Length;
            if (len < MaxOrder)
            {
                var toDupe = items[^1];
                var dupes = DeepDuplicateItem(toDupe, MaxOrder - len);
                itemsToAdd = itemsToAdd.Concat(dupes).ToArray();

                if (catalogue)
                    itemsToAdd[len] = new Item(Item.NONE);
            }
        }

        public static void StackToMax(Item[] itemSet)
        {
            foreach (var it in itemSet)
            {
                if (getMaxStack(it, out var max) && max != 1)
                {
                    it.Count = (ushort)(max - 1);
                }
            }
        }

        public static void StackToMax(IReadOnlyCollection<Item> itemSet)
            => StackToMax(itemSet.ToArray());

        public static Item[] DeepDuplicateItem(Item it, int count)
        {
            var ret = new Item[count];
            for (int i = 0; i < count; ++i)
            {
                ret[i] = new Item();
                ret[i].CopyFrom(it);
            }
            return ret;
        }

        static bool getMaxStack(Item id, out int max)
        {
            if (StackableFlowers.Contains(id.ItemId))
            {
                max = 10;
                return true;
            }

            bool canStack = ItemInfo.TryGetMaxStackCount(id, out var maxStack);
            max = maxStack;
            return canStack;
        }

        // Example: certain flowers can be stacked to 10
        public static readonly ushort[] StackableFlowers =
        {
            2304, 2305, 2867, 2871, 2875, 2979, 2883, 2887, 2891, 2895, 2899, 2903,
            2907, 2911, 2915, 2919, 2923, 2927, 2931, 2935, 2939, 2943, 2947, 2951,
            2955, 2959, 2963, 2979, 2983, 2987, 2991, 2995, 2999, 3709, 3713, 3717,
            3720, 3723, 3727, 3730, 3734, 3738, 3741, 3744, 3748, 3751, 3754, 3758,
            3762, 3765, 3768, 5175
        };
    }
}

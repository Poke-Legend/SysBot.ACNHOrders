using System;
using System.Collections.Generic;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class InventoryValidator
    {
        private const int PocketSize = Item.SIZE * 20;
        private const int TotalSize = (PocketSize + 0x18) * 2;
        private const int OffsetShift = -0x18 - PocketSize;

        public static (uint Offset, int Length) GetOffsetLength(uint slot1) =>
            ((uint)((int)slot1 + OffsetShift), TotalSize);

        public static bool ValidateItemBinary(byte[] data)
        {
            // Validate unlocked slot count: should be 0, 10, or 20
            var bagCount = BitConverter.ToUInt32(data, PocketSize);
            if (bagCount > 20 || bagCount % 10 != 0)
                return false;

            // Validate pocket count: should be 20
            var pocketCount = BitConverter.ToUInt32(data, PocketSize + 0x18 + PocketSize);
            if (pocketCount != 20)
                return false;

            // Validate item wheel bindings and check for duplicates
            var boundItems = new HashSet<byte>();
            return ValidateBindList(data, PocketSize + 4, boundItems) &&
                   ValidateBindList(data, PocketSize + 4 + (PocketSize + 0x18), boundItems);
        }

        private static bool ValidateBindList(byte[] data, int bindStart, HashSet<byte> boundItems)
        {
            for (int i = 0; i < 20; i++)
            {
                var bind = data[bindStart + i];
                if (bind == 0xFF) continue;
                if (bind > 7 || !boundItems.Add(bind))
                    return false;
            }
            return true;
        }
    }
}

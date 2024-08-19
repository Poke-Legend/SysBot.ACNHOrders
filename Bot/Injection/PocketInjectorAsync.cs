using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public class PocketInjectorAsync
    {
        public readonly Item DroppableOnlyItem = new(0x9C9); // Gold nugget

        private readonly ISwitchConnectionAsync Bot;
        public bool Connected => Bot.Connected;

        public uint WriteOffset { private get; set; }
        public bool ValidateEnabled { get; set; } = true;

        private const int PocketSize = Item.SIZE * 20;
        private const int DataSize = (PocketSize + 0x18) * 2;
        private const int Shift = -0x18 - PocketSize;

        private uint DataOffset => (uint)(WriteOffset + Shift);

        public PocketInjectorAsync(ISwitchConnectionAsync bot, uint writeOffset)
        {
            Bot = bot;
            WriteOffset = writeOffset;
        }

        public async Task<(bool, byte[])> ReadValidateAsync(CancellationToken token)
        {
            var data = await Bot.ReadBytesAsync(DataOffset, DataSize, token).ConfigureAwait(false);
            var validated = Validate(data);
            return (validated, data);
        }

        public async Task<(InjectionResult, Item[])> Read(CancellationToken token)
        {
            var (valid, data) = await ReadValidateAsync(token);
            if (!valid) return (InjectionResult.FailValidate, Array.Empty<Item>());

            var seqItems = GetEmptyInventory();
            var p1 = Item.GetArray(data.Slice(0, PocketSize));
            var p2 = Item.GetArray(data.Slice(PocketSize + 0x18, PocketSize));

            Array.Copy(p1, 0, seqItems, 0, p1.Length);
            Array.Copy(p2, 0, seqItems, 20, p2.Length);

            return (InjectionResult.Success, seqItems);
        }

        public async Task<InjectionResult> Write(Item[] items, CancellationToken token)
        {
            var (valid, data) = await ReadValidateAsync(token);
            if (!valid) return InjectionResult.FailValidate;

            var orig = (byte[])data.Clone();
            var pocket1 = items.Take(20).ToArray();
            var pocket2 = items.Skip(20).ToArray();
            var p1 = Item.SetArray(pocket1);
            var p2 = Item.SetArray(pocket2);

            p1.CopyTo(data, 0);
            p2.CopyTo(data, PocketSize + 0x18);

            if (data.SequenceEqual(orig)) return InjectionResult.Same;

            await Bot.WriteBytesAsync(data, DataOffset, token);
            return InjectionResult.Success;
        }

        public async Task Write40(Item item, CancellationToken token)
        {
            var itemSet = MultiItem.DeepDuplicateItem(item, 40);
            await Write(itemSet, token).ConfigureAwait(false);
        }

        public static Item[] GetEmptyInventory(int invCount = 40)
        {
            return Enumerable.Repeat(new Item(), invCount).ToArray();
        }

        private bool Validate(byte[] data)
        {
            return !ValidateEnabled || ValidateItemBinary(data);
        }

        private static bool ValidateItemBinary(byte[] data)
        {
            var bagCount = BitConverter.ToUInt32(data, PocketSize);
            if (bagCount > 20 || bagCount % 10 != 0) return false;

            var pocketCount = BitConverter.ToUInt32(data, PocketSize + 0x18 + PocketSize);
            if (pocketCount != 20) return false;

            var bound = new List<byte>();
            return ValidateBindList(data, PocketSize + 4, bound) && ValidateBindList(data, PocketSize + 4 + PocketSize + 0x18, bound);
        }

        private static bool ValidateBindList(byte[] data, int bindStart, ICollection<byte> bound)
        {
            for (int i = 0; i < 20; i++)
            {
                var bind = data[bindStart + i];
                if (bind == 0xFF) continue;
                if (bind > 7 || bound.Contains(bind)) return false;
                bound.Add(bind);
            }
            return true;
        }
    }
}

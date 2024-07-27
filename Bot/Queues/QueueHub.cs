using NHSE.Core;
using System.Collections.Concurrent;

namespace SysBot.ACNHOrders
{
    public class QueueHub
    {
        public ConcurrentQueue<IACNHOrderNotifier<Item>> Orders { get; } = new();

        public static QueueHub CurrentInstance { get; } = new();

        private QueueHub() { }
    }
}

using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public class ChannelManager
    {
        private readonly string _filePath = "channelID.json";

        public List<ulong> LoadChannels()
        {
            if (!File.Exists(_filePath))
            {
                var data = new Dictionary<string, List<ulong>> { { "channels", new List<ulong>() } };
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(data, Formatting.Indented));
                return new List<ulong>();
            }

            var json = File.ReadAllText(_filePath);
            var channelsData = JsonConvert.DeserializeObject<Dictionary<string, List<ulong>>>(json);

            // Add null checks
            if (channelsData != null && channelsData.ContainsKey("channels"))
            {
                return channelsData["channels"] ?? new List<ulong>();
            }

            return new List<ulong>();
        }

        public void SaveChannels(List<ulong> channels)
        {
            var data = new Dictionary<string, List<ulong>> { { "channels", channels } };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
    }
}

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
                // Initialize and save empty channels data
                SaveChannels(new List<ulong>());
                return new List<ulong>();
            }

            var json = File.ReadAllText(_filePath);
            var channelsData = JsonConvert.DeserializeObject<Dictionary<string, List<ulong>>>(json);

            // Return the channel list if present, otherwise return an empty list
            return channelsData?.GetValueOrDefault("channels") ?? new List<ulong>();
        }

        public void SaveChannels(List<ulong> channels)
        {
            // Serialize and write channels data to file
            var data = new Dictionary<string, List<ulong>> { { "channels", channels } };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
    }
}

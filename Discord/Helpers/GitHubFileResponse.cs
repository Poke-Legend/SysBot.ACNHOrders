using System.Text.Json.Serialization;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public class GitHubFileResponse
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

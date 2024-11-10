using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders.Discord.Helpers
{
    public static class GitHubApi
    {
        // API URL constants for different files
        public const string SudoApiUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/sudo.json";
        public const string UserBanApiUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/userban.json";
        public const string ServerBanApiUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/serverban.json";
        public const string ChannelListApiUrl = "https://api.github.com/repos/Poke-Legend/ACNH-DATABASE/contents/whitelistchannel.json";

        // GitHub Token for authentication (Replace with actual token or retrieve from environment variables for better security)
        private const string GitHubToken = " ";

        public static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ACNHOrdersBot");
            if (!string.IsNullOrEmpty(GitHubToken))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GitHubToken);
            }
            return client;
        }

        public static async Task<string?> FetchFileContentAsync(string apiUrl)
        {
            using var client = CreateHttpClient();
            try
            {
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var fileData = JsonSerializer.Deserialize<GitHubFileData>(json);
                    return fileData?.Content != null ? Encoding.UTF8.GetString(Convert.FromBase64String(fileData.Content)) : null;
                }
                Console.WriteLine($"Error fetching file: {response.StatusCode}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching file content: {ex.Message}");
                return null;
            }
        }

        public static async Task<string?> GetFileShaAsync(string apiUrl)
        {
            using var client = CreateHttpClient();
            try
            {
                var response = await client.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var fileInfo = JsonSerializer.Deserialize<GitHubFileData>(responseContent);
                    return fileInfo?.Sha;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Console.WriteLine("File not found on GitHub. It may need to be created.");
                    return null;
                }
                else
                {
                    Console.WriteLine($"SHA retrieval failed: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving SHA: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> UpdateFileAsync(string content, string commitMessage, string apiUrl, string? sha = null)
        {
            using var client = CreateHttpClient();
            try
            {
                var updatePayload = new
                {
                    message = commitMessage,
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                    sha // Include the SHA to overwrite the existing file
                };

                var payloadJson = JsonSerializer.Serialize(updatePayload);
                var requestContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                Console.WriteLine("Attempting to update file on GitHub...");
                Console.WriteLine($"API URL: {apiUrl}");
                Console.WriteLine($"Payload: {payloadJson}");

                var response = await client.PutAsync(apiUrl, requestContent);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("File updated successfully on GitHub.");
                    return true;
                }
                else
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error updating file on GitHub: {response.StatusCode} - {errorResponse}");
                    return false;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error updating file on GitHub: {ex.Message}");
                return false;
            }
        }
        private class GitHubFileData
        {
            [JsonPropertyName("sha")]
            public string? Sha { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }
    }
}

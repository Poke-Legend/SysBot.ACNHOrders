using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class WebConfig
    {
        /// <summary> HTTP or HTTPS Endpoint</summary>
        public string URIEndpoint { get; set; } = string.Empty;

        /// <summary> The Auth ID </summary>
        public string AuthID { get; set; } = string.Empty;

        /// <summary> The Auth Token or Password </summary>
        public string AuthTokenOrString { get; set; } = string.Empty;

        /// <summary>
        /// Validates the WebConfig properties to ensure all required fields are set.
        /// </summary>
        /// <returns>True if the configuration is valid; otherwise, false.</returns>
        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(URIEndpoint) &&
                   !string.IsNullOrWhiteSpace(AuthID) &&
                   !string.IsNullOrWhiteSpace(AuthTokenOrString);
        }

        /// <summary>
        /// Tests the connection to the configured URI endpoint.
        /// </summary>
        /// <returns>A task representing the asynchronous operation, with a result indicating success or failure.</returns>
        public async Task<bool> TestConnectionAsync()
        {
            if (!IsValid())
            {
                Console.WriteLine("WebConfig validation failed. Ensure all required fields are set.");
                return false;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("AuthID", AuthID);
                client.DefaultRequestHeaders.Add("AuthToken", AuthTokenOrString);

                HttpResponseMessage response = await client.GetAsync(URIEndpoint).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Connection test succeeded.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Connection test failed. Status Code: {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Connection test failed with an exception: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Displays the WebConfig details as a string.
        /// </summary>
        /// <returns>A formatted string representing the WebConfig.</returns>
        public override string ToString()
        {
            return $"URIEndpoint: {URIEndpoint}, AuthID: {AuthID}, AuthToken: {new string('*', AuthTokenOrString.Length)}";
        }
    }
}

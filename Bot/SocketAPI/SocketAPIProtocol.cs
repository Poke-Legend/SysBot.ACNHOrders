using System;
using System.Text;
using System.Text.Json;

namespace SocketAPI
{
    public sealed class SocketAPIProtocol
    {
        // Create a single instance of JsonSerializerOptions for reuse and consistency
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false, // For more compact JSON representation
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Given an inbound JSON-formatted string message, this method returns a `SocketAPIRequest` instance.
        /// Returns `null` if the input message is invalid JSON or if `endpoint` is missing.
        /// </summary>
        public static SocketAPIRequest? DecodeMessage(string message)
        {
            try
            {
                // Deserialize JSON to SocketAPIRequest object using static options
                var request = JsonSerializer.Deserialize<SocketAPIRequest>(message, JsonOptions);

                // Return null if the endpoint is missing
                if (request?.Endpoint == null)
                {
                    Logger.LogError($"Missing or invalid endpoint in request: {message}");
                    return null;
                }

                return request;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not deserialize inbound request: {message}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Given the message type and input string, this method returns an encoded message ready to be sent to a client.
        /// The JSON-encoded message is terminated by "\0\0".
        /// Do not send messages of length > 2^16 bytes (or your OS's default TCP buffer size)! The messages would get TCP-fragmented.
        /// </summary>
        public static string? EncodeMessage(SocketAPIMessage message)
        {
            try
            {
                // Serialize the message and append the null terminator
                var serializedMessage = JsonSerializer.Serialize(message, JsonOptions);

                // Use StringBuilder for efficient concatenation when adding the terminator
                var result = new StringBuilder(serializedMessage, serializedMessage.Length + 2)
                    .Append("\0\0")
                    .ToString();

                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Could not serialize outbound message: {message}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Given the input message, this method retrieves the message type as per protocol specification (1.)
        /// </summary>
        private static SocketAPIMessageType GetMessageTypeFromMessage(string type)
        {
            // Attempt to parse the type string into a SocketAPIMessageType enum
            if (Enum.TryParse(type, true, out SocketAPIMessageType messageType))
            {
                return messageType;
            }

            Logger.LogError($"Invalid message type: {type}");
            throw new ArgumentException("Invalid message type", nameof(type));
        }
    }
}

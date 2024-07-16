using System;
using System.Text.Json;

namespace SocketAPI
{
    public sealed class SocketAPIProtocol
    {
        /// <summary>
        /// Given an inbound JSON-formatted string message, this method returns a `SocketAPIRequest` instance.
        /// Returns `null` if the input message is invalid JSON or if `endpoint` is missing.
        /// </summary>
        public static SocketAPIRequest? DecodeMessage(string message)
        {
            try
            {
                var request = JsonSerializer.Deserialize<SocketAPIRequest>(message);
                return request?.Endpoint == null ? null : request;
            }
            catch (JsonException ex)
            {
                Logger.LogError($"Could not deserialize inbound request: {message}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Given the message type and input string, this method returns an encoded message ready to be sent to a client.
        /// The JSON-encoded message is terminated by "\0\0".
        /// </summary>
        public static string? EncodeMessage(SocketAPIMessage message)
        {
            try
            {
                return JsonSerializer.Serialize(message) + "\0\0";
            }
            catch (JsonException ex)
            {
                Logger.LogError($"Could not serialize outbound message: {message}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Retrieves the message type as per protocol specification.
        /// </summary>
        public static SocketAPIMessageType GetMessageTypeFromMessage(string type)
        {
            return (SocketAPIMessageType)Enum.Parse(typeof(SocketAPIMessageType), type, true);
        }
    }
}

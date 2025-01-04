using System;
using System.Text;
using System.Text.Json;

namespace SocketAPI
{
    /// <summary>
    /// Provides JSON-based encoding/decoding utilities for SocketAPI requests and messages.
    /// </summary>
    public sealed class SocketAPIProtocol
    {
        // Reusable JsonSerializerOptions for consistent, efficient JSON (de)serialization.
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Deserializes a JSON string into a SocketAPIRequest object.
        /// Returns null if deserialization fails or if 'Endpoint' is null.
        /// </summary>
        /// <param name="message">Inbound JSON string</param>
        /// <returns>A SocketAPIRequest instance, or null if invalid.</returns>
        public static SocketAPIRequest? DecodeMessage(string message)
        {
            try
            {
                var request = JsonSerializer.Deserialize<SocketAPIRequest>(message, JsonOptions);

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
        /// Serializes a SocketAPIMessage to JSON, appends a double null terminator ("\0\0") for the protocol,
        /// and returns the resulting string. If serialization fails, returns null.
        /// </summary>
        /// <param name="message">The SocketAPIMessage to encode</param>
        /// <returns>A string with JSON and trailing "\0\0", or null if serialization fails.</returns>
        public static string? EncodeMessage(SocketAPIMessage message)
        {
            try
            {
                var serializedMessage = JsonSerializer.Serialize(message, JsonOptions);

                // Append null terminators
                var result = new StringBuilder(serializedMessage, serializedMessage.Length + 2)
                    .Append("\0\0")
                    .ToString();

                return result;
            }
            catch (Exception ex)
            {
                // If there's an error serializing, log it and return null
                Logger.LogError($"Could not serialize outbound message: {message}. Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Attempts to parse a string into a SocketAPIMessageType enum.
        /// Throws an exception if parsing fails.
        /// </summary>
        /// <param name="type">The string representing the message type</param>
        /// <returns>The SocketAPIMessageType enum value</returns>
        /// <exception cref="ArgumentException">Thrown if the string cannot be parsed into the enum</exception>
        private static SocketAPIMessageType GetMessageTypeFromMessage(string type)
        {
            if (Enum.TryParse(type, true, out SocketAPIMessageType messageType))
            {
                return messageType;
            }

            Logger.LogError($"Invalid message type: {type}");
            throw new ArgumentException("Invalid message type", nameof(type));
        }
    }
}

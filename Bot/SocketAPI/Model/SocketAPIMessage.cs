namespace SocketAPI
{
    /// <summary>
    /// Represents a serializable response to return to the client.
    /// </summary>
    public class SocketAPIMessage
    {
        public SocketAPIMessage() { }

        public SocketAPIMessage(object? value, string? error)
        {
            this.Value = value;
            this.Error = error;
        }

        /// <summary>
        /// Describes whether the request completed successfully or not.
        /// </summary>
        public string Status => this.Error != null ? "error" : "okay";

        /// <summary>
        /// The unique identifier of the associated request.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Describes the type of response; i.e. event or response.
        /// Wrapper property used for encoding purposes.
        /// </summary>
        public string? _Type => this.Type?.ToString().ToLower();

        /// <summary>
        /// Describes the type of response; i.e. event or response. 
        /// </summary>
        public SocketAPIMessageType? Type { get; set; }

        /// <summary>
        /// If an error occurred while processing the client's request, this property would contain the error message.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The actual body of the response, if any.
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// Serializes this object to a JSON string.
        /// </summary>
        /// <returns>A JSON string representation of this object.</returns>
        public string Serialize()
        {
            return System.Text.Json.JsonSerializer.Serialize(this);
        }

        /// <summary>
        /// Creates a `SocketAPIMessage` populated with the supplied value object.
        /// </summary>
        /// <param name="value">The value to include in the message.</param>
        /// <returns>A new `SocketAPIMessage` instance with the given value.</returns>
        public static SocketAPIMessage FromValue(object? value)
        {
            return new SocketAPIMessage(value, null);
        }

        /// <summary>
        /// Creates a `SocketAPIMessage` populated with the supplied error message.
        /// </summary>
        /// <param name="errorMessage">The error message to include in the message.</param>
        /// <returns>A new `SocketAPIMessage` instance with the given error message.</returns>
        public static SocketAPIMessage FromError(string errorMessage)
        {
            return new SocketAPIMessage(null, errorMessage);
        }

        public override string ToString()
        {
            return $"SocketAPI.SocketAPIMessage (id: {this.Id}) - status: {this.Status}, type: {this.Type}, value: {this.Value}, error: {this.Error}";
        }
    }
}

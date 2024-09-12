namespace SocketAPI
{
    public class SocketAPIMessage
    {
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false // Set to true if indented JSON is required for readability
        };

        public SocketAPIMessage() { }

        public SocketAPIMessage(object? value, string? error)
        {
            Value = value;
            Error = error;
        }

        public string Status => Error != null ? "error" : "okay";
        public string? Id { get; set; }
        public string? _Type => Type?.ToString().ToLower();
        public SocketAPIMessageType? Type { get; set; }
        public string? Error { get; set; }
        public object? Value { get; set; }

        /// <summary>
        /// Optimized serialization method with predefined options.
        /// </summary>
        public string Serialize()
        {
            return System.Text.Json.JsonSerializer.Serialize(this, _jsonOptions);
        }

        public static SocketAPIMessage FromValue(object? value) => new SocketAPIMessage(value, null);
        public static SocketAPIMessage FromError(string errorMessage) => new SocketAPIMessage(null, errorMessage);

        public override string ToString() =>
            $"SocketAPI.SocketAPIMessage (id: {Id}) - status: {Status}, type: {Type}, value: {Value}, error: {Error}";
    }
}

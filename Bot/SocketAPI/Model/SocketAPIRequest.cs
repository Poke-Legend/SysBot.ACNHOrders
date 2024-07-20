using System;

namespace SocketAPI
{
    [Serializable]
    public sealed class SocketAPIRequest
    {
        public string? Id { get; set; }
        public string? Endpoint { get; set; }
        public string? Args { get; set; }

        public SocketAPIRequest() { }

        public override string ToString() =>
            $"SocketAPI.SocketAPIRequest (id: {Id}) - endpoint: {Endpoint}, args: {Args}";
    }
}

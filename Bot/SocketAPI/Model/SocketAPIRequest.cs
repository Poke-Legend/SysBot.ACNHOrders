using System;
using System.Text;

namespace SocketAPI
{
    [Serializable]
    public sealed class SocketAPIRequest
    {
        /// <summary>
        /// The unique identifier for the request.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Represents the name of the endpoint to remotely execute and from which to fetch the result.
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// The JSON-formatted arguments string to pass to the endpoint.
        /// </summary>
        public string Args { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SocketAPIRequest"/> class.
        /// </summary>
        /// <param name="id">Unique identifier for the request.</param>
        /// <param name="endpoint">The endpoint to be called.</param>
        /// <param name="args">The JSON-formatted arguments.</param>
        public SocketAPIRequest(string id, string endpoint, string args)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            Args = args ?? throw new ArgumentNullException(nameof(args));
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("SocketAPI.SocketAPIRequest (id: ").Append(Id).Append(") - endpoint: ")
              .Append(Endpoint).Append(", args: ").Append(Args);
            return sb.ToString();
        }
    }
}

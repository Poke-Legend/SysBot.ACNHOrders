namespace SocketAPI
{
    /// <summary>
    /// Represents the type of messages that can be sent or received in the Socket API.
    /// </summary>
    public enum SocketAPIMessageType
    {
        /// <summary>
        /// Indicates a response message.
        /// </summary>
        Response,

        /// <summary>
        /// Indicates an event message.
        /// </summary>
        Event,

        /// <summary>
        /// Indicates an invalid message type.
        /// </summary>
        Invalid,
    }
}

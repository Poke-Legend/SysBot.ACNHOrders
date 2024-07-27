using System;

namespace SysBot.ACNHOrders.Bot.SocketAPI.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    /// <summary>
    /// Marks a method as a SocketAPIServer endpoint, reachable from remote clients.
    /// The attributed method must:
    /// - be static
    /// - have a return type of `object?`
    /// - have a single parameter of type `string`.
    /// </summary>
    public class SocketAPIEndpoint : Attribute { }
}
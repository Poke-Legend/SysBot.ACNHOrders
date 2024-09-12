using System;

namespace SysBot.ACNHOrders.Signalr
{
    public class SignalrCrossBot
    {
        private readonly CrossBot _bot;
        private readonly string _uri;
        private readonly string _authID;
        private readonly string _authString;

        private readonly IDodoRestoreNotifier _webNotifierInstance;

        /// <summary>
        /// Initializes a new instance of the <see cref="SignalrCrossBot"/> class.
        /// </summary>
        /// <param name="settings">The configuration settings for the web connection.</param>
        /// <param name="bot">The bot instance to be used.</param>
        public SignalrCrossBot(WebConfig settings, CrossBot bot)
        {
            _bot = bot ?? throw new ArgumentNullException(nameof(bot));
            _uri = settings?.URIEndpoint ?? throw new ArgumentNullException(nameof(settings.URIEndpoint));
            _authID = settings.AuthID ?? throw new ArgumentNullException(nameof(settings.AuthID));
            _authString = settings.AuthTokenOrString ?? throw new ArgumentNullException(nameof(settings.AuthTokenOrString));

            _webNotifierInstance = new SignalRNotify(_authID, _authString, _uri);
            _bot.DodoNotifiers.Add(_webNotifierInstance);
        }
    }
}

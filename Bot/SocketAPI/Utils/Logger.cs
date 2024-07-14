using System;
using SysBot.Base;

namespace SocketAPI
{
    public static class Logger
    {
        /// <summary>
        /// Whether logs are enabled or not.
        /// </summary>
        private static bool logsEnabled = true;

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="ignoreDisabled">If set to true, ignores the log enabled flag.</param>
        public static void LogInfo(string message, bool ignoreDisabled = false)
        {
            if (!logsEnabled && !ignoreDisabled)
                return;

            LogUtil.LogInfo(message, nameof(SocketAPI));
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="ignoreDisabled">If set to true, ignores the log enabled flag.</param>
        public static void LogWarning(string message, bool ignoreDisabled = false)
        {
            if (!logsEnabled && !ignoreDisabled)
                return;

            LogUtil.LogInfo(message, $"{nameof(SocketAPI)} Warning");
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="ignoreDisabled">If set to true, ignores the log enabled flag.</param>
        public static void LogError(string message, bool ignoreDisabled = false)
        {
            if (!logsEnabled && !ignoreDisabled)
                return;

            LogUtil.LogError(message, nameof(SocketAPI));
        }

        /// <summary>
        /// Disables logging.
        /// </summary>
        public static void DisableLogs()
        {
            logsEnabled = false;
        }

        /// <summary>
        /// Enables logging.
        /// </summary>
        public static void EnableLogs()
        {
            logsEnabled = true;
        }
    }
}

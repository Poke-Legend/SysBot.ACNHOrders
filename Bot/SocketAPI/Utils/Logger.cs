using System;
using SysBot.Base;

namespace SocketAPI
{
    public static class Logger
    {
        private static bool logsEnabled = true;

        public static void LogInfo(string message, bool ignoreDisabled = false)
        {
            Log(message, nameof(SocketAPI), LogUtil.LogInfo, ignoreDisabled);
        }

        public static void LogWarning(string message, bool ignoreDisabled = false)
        {
            Log(message, $"{nameof(SocketAPI)} Warning", LogUtil.LogInfo, ignoreDisabled);
        }

        public static void LogError(string message, bool ignoreDisabled = false)
        {
            Log(message, nameof(SocketAPI), LogUtil.LogError, ignoreDisabled);
        }

        public static void DisableLogs()
        {
            logsEnabled = false;
        }

        public static void EnableLogs()
        {
            logsEnabled = true;
        }

        private static void Log(string message, string context, Action<string, string> logAction, bool ignoreDisabled)
        {
            if (!logsEnabled && !ignoreDisabled)
                return;

            logAction(message, context);
        }
    }
}

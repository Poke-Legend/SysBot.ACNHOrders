using System;
using SysBot.Base;

namespace SocketAPI
{
    public static class Logger
    {
        private static bool _logsEnabled = true;

        public static void LogInfo(string message)
        {
            if (_logsEnabled)
                Console.WriteLine($"INFO: {message}");
        }

        public static void LogError(string message)
        {
            if (_logsEnabled)
                Console.WriteLine($"ERROR: {message}");
        }

        public static void DisableLogs()
        {
            _logsEnabled = false;
        }

        public static void EnableLogs()
        {
            _logsEnabled = true;
        }
    }
}
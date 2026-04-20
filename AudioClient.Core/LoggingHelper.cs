using System;
using System.IO;
using System.Runtime.CompilerServices;
using Elements.Core;

namespace AudioClient.Core;

public static class LoggingHelper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Initialize(string appDir, Action<string, string, string>? extraHandler = null)
    {
        string logDir = Path.Combine(appDir, "Logs");
        Directory.CreateDirectory(logDir);
        string logname = UniLog.GenerateLogName(FrooxEngine.Engine.VersionNumber + "AudioClient");
        var logStream = File.CreateText(Path.Combine(logDir, logname));
        object logLock = new object();

        void WriteLog(string prefix, string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{prefix}] {msg}";
            lock (logLock)
            {
                logStream.WriteLine(line);
                logStream.Flush();
            }
            extraHandler?.Invoke(prefix, msg, line);
        }

        UniLog.OnLog += msg => WriteLog("INFO", msg);
        UniLog.OnWarning += msg => WriteLog("WARN", msg);
        UniLog.OnError += msg => WriteLog("ERR", msg);
        UniLog.OnFlush += () => { lock (logLock) logStream.Flush(); };
    }
}

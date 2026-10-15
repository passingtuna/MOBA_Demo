using System;

// headless ServerScene — Unity Debug 대신 콘sole
internal static class ServerLog
{
    public static void Info(string message) => Console.WriteLine(message);

    public static void Warning(string message) => Console.WriteLine("[WARN] " + message);

    public static void Error(string message) => Console.WriteLine("[ERROR] " + message);
}

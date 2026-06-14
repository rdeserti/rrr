using System;
using System.Diagnostics;

namespace rrr.App;

/// <summary>
/// Minimal logger. <see cref="Debug"/> calls are compiled away entirely in
/// Release builds (via <see cref="ConditionalAttribute"/>) and printed to the
/// console in Debug builds.
/// </summary>
public static class Log
{
    [Conditional("DEBUG")]
    public static void Debug(string message)
    {
        Console.WriteLine(message);
    }

    [Conditional("DEBUG")]
    public static void Debug(string format, params object[] args)
    {
        Console.WriteLine(format, args);
    }
}

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace rrr.Core;

public sealed class Profiler
{
    private const int MaxMarks = 64;

    private readonly string[] _names;
    private readonly long[] _ticks;

    private int _count;

    private long _startTicks;
    private long _lastTicks;

    public Profiler()
    {
        _names = new string[MaxMarks];
        _ticks = new long[MaxMarks];
    }

    public void Start()
    {
        _count = 0;

        _startTicks =
            Stopwatch.GetTimestamp();

        _lastTicks = _startTicks;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Mark(string name)
    {
        if (_count >= MaxMarks)
            throw new InvalidOperationException(
                $"Maximum number of marks ({MaxMarks}) exceeded.");

        long now =
            Stopwatch.GetTimestamp();

        _names[_count] = name;
        _ticks[_count] = now - _lastTicks;

        _lastTicks = now;
        _count++;
    }

    public void Dump()
    {
        Console.WriteLine();
        Console.WriteLine("===== PROFILER =====");

        double totalMs = 0.0;

        for (int i = 0; i < _count; i++)
        {
            double ms =
                _ticks[i] * 1000.0 /
                Stopwatch.Frequency;

            totalMs += ms;

            Console.WriteLine(
                $"{_names[i],-20} {ms,10:F3} ms");
        }

        long totalTicks =
            _lastTicks - _startTicks;

        double measuredMs =
            totalTicks * 1000.0 /
            Stopwatch.Frequency;

        Console.WriteLine("------------------------------");
        Console.WriteLine(
            $"TOTAL                {measuredMs,10:F3} ms");
        Console.WriteLine();
    }
}
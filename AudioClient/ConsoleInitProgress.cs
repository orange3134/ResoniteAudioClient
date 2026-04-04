using System;
using FrooxEngine;

namespace AudioClient;

public class ConsoleInitProgress : IEngineInitProgress
{
    public int FixedPhaseIndex { get; private set; }

    public void SetFixedPhase(string phase)
    {
        FixedPhaseIndex++;
        Console.WriteLine($"[Init Phase {FixedPhaseIndex}] {phase}");
    }

    public void SetSubphase(string subphase, bool alwaysShow = false)
    {
        if (alwaysShow || FixedPhaseIndex <= 2) // Just so it doesn't spam too much, but alwaysShow is respected
        {
            Console.WriteLine($"  -> {subphase}");
        }
    }

    public void EngineReady()
    {
        Console.WriteLine("=================================");
        Console.WriteLine(" FROOXENGINE BOOTSTRAP COMPLETE! ");
        Console.WriteLine("=================================");
    }
}

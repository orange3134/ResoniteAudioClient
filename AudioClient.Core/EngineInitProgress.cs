using System;
using FrooxEngine;

namespace AudioClient.Core;

public class EngineInitProgress : IEngineInitProgress
{
    private readonly Action<string> _onPhase;
    private readonly Action<string>? _onSubphase;
    private readonly Action? _onReady;

    public int FixedPhaseIndex { get; private set; }

    public EngineInitProgress(Action<string> onPhase, Action<string>? onSubphase = null, Action? onReady = null)
    {
        _onPhase = onPhase;
        _onSubphase = onSubphase;
        _onReady = onReady;
    }

    public void SetFixedPhase(string phase)
    {
        FixedPhaseIndex++;
        _onPhase($"[Init Phase {FixedPhaseIndex}] {phase}");
    }

    public void SetSubphase(string subphase, bool alwaysShow = false)
    {
        if (alwaysShow || FixedPhaseIndex <= 2)
            _onSubphase?.Invoke($"  -> {subphase}");
    }

    public void EngineReady()
    {
        _onReady?.Invoke();
    }
}

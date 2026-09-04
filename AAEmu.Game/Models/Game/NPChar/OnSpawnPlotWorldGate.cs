namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// When to World-run an OnSpawn plot graph under ZoneAuthority (dedic is silent).
/// Restricted to tower-priority zone mirrors. A plot id is required; <c>plot_only</c> is not —
/// Lusca stage skills attach a plot with plot_only false and no direct skill_effects.
/// Skills that still have direct skill_effects keep the old skip (e.g. Crimson seed open FX).
/// </summary>
public static class OnSpawnPlotWorldGate
{
    public static bool ShouldRun(
        bool zoneAuthority,
        bool isZoneMirror,
        bool isPriorityMirror,
        bool hasPlot,
        bool plotOnly,
        int directSkillEffectCount)
    {
        if (!zoneAuthority || !isZoneMirror || !isPriorityMirror || !hasPlot)
            return false;
        if (plotOnly)
            return true;
        return directSkillEffectCount <= 0;
    }
}

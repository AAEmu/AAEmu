namespace AAEmu.Game.Models.Game.NPChar;

/// <summary>
/// When to World-run an OnSpawn plot graph under ZoneAuthority (dedic is silent).
/// A plot id is required. A <c>plot_only</c> graph runs for every zone mirror: the dedic never fires
/// those, and every NPC that carries one is an event script (summon stages, greetings, a dungeon's
/// enemy-summon trigger, the hunter that leaps on a summoned army). A graph without <c>plot_only</c>
/// stays restricted to tower-priority mirrors — Lusca stage skills attach a plot with plot_only false
/// and no direct skill_effects. Skills that still have direct skill_effects keep the old skip
/// (e.g. Crimson seed open FX).
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
        if (!zoneAuthority || !isZoneMirror || !hasPlot)
            return false;
        if (plotOnly)
            return true;
        return isPriorityMirror && directSkillEffectCount <= 0;
    }
}

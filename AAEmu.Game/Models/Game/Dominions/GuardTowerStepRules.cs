using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Dominions;

/// <summary>
/// Pure guard-tower step decisions. The shipped rows are caps and a buff, not a spawn list; callers
/// must therefore supply the live wall/gate counts and apply the returned buff explicitly.
/// </summary>
public static class GuardTowerStepRules
{
    public static GuardTowerStep RequireStep(IReadOnlyList<GuardTowerStep> steps, int step)
    {
        if (step <= 0)
            throw new ArgumentOutOfRangeException(nameof(step), step, "Guard-tower steps start at one.");

        var matches = steps?.Where(candidate => candidate.Step == step).ToArray() ?? [];
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Guard-tower step {step} is not present in content."),
            _ => throw new InvalidOperationException($"Guard-tower step {step} is duplicated in content.")
        };
    }

    public static bool MayPlaceWall(GuardTowerStep step, int currentWalls) =>
        step is not null && currentWalls >= 0 && currentWalls < step.NumWalls;

    public static bool MayPlaceGate(GuardTowerStep step, int currentGates) =>
        step is not null && currentGates >= 0 && currentGates < step.NumGates;

    public static IReadOnlyList<uint> StepBuffs(IReadOnlyList<GuardTowerStep> steps) =>
        (steps ?? []).Where(step => step.BuffId != 0).Select(step => step.BuffId).Distinct().ToArray();
}

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Compact growth scales are permille (1000 = 1.0). A row with both start and end at 0 is a
/// timer-only arm (guild worldgate countdown) — applying 0 would hide the mesh.
/// </summary>
public static class DoodadGrowthScaleRules
{
    public static bool IsTimerOnly(int startScalePermille, int endScalePermille) =>
        startScalePermille == 0 && endScalePermille == 0;

    public static float ResolveScale(int scalePermille, int peerScalePermille, float currentScale)
    {
        if (IsTimerOnly(scalePermille, peerScalePermille))
            return currentScale > 0f ? currentScale : 1f;

        return scalePermille / 1000f;
    }
}

using System;
using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.Game.Models.Game.Char;

public class Actability
{
    public uint Id { get; set; }
    public ActabilityTemplate Template { get; set; }
    public int Point { get; set; }
    public byte Step { get; set; }

    // These are 3.x values, and might not be correct for 1.2
    private static readonly float[] s_expMultipliers       = [1.00f, 1.20f, 1.40f, 1.60f, 1.80f, 2.00f, 2.20f, 2.40f, 2.60f, 2.80f, 3.00f, 3.30f];
    private static readonly float[] s_laborCostMultipliers = [1.00f, 1.00f, 0.95f, 0.90f, 0.85f, 0.80f, 0.80f, 0.80f, 0.80f, 0.75f, 0.70f, 0.60f];
    private static readonly float[] s_timeMultipliers      = [1.00f, 0.97f, 0.94f, 0.94f, 0.94f, 0.88f, 0.88f, 0.88f, 0.84f, 0.84f, 0.80f, 0.74f];

    public Actability(ActabilityTemplate template)
    {
        Id = template.Id;
        Template = template;
    }

    /// <summary>
    /// Gets Exp multiplier for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetExpMultiplier()
    {
        return s_expMultipliers[Math.Clamp(Step, 0, s_expMultipliers.Length - 1)];
    }

    /// <summary>
    /// Gets Labor Cost *multiplier* for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetLaborCostMultiplier()
    {
        return s_laborCostMultipliers[Math.Clamp(Step, 0, s_laborCostMultipliers.Length - 1)];
    }

    /// <summary>
    /// Gets Production Time *multiplier* for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetProductionTimeMultiplier()
    {
        return s_timeMultipliers[Math.Clamp(Step, 0, s_timeMultipliers.Length - 1)];
    }

    /// <summary>
    /// Gets a multiplier to use for skill specific drops
    /// </summary>
    /// <returns></returns>
    public float GetLootMultiplier()
    {
        return GetExpMultiplier();
    }
}

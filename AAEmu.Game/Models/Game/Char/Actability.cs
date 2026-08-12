using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char.Templates;

namespace AAEmu.Game.Models.Game.Char;

public class Actability(ActabilityTemplate template)
{
    public uint Id { get; init; } = template.Id;
    public ActabilityTemplate Template { get; set; } = template;
    public int Point { get; set; }
    public byte Step { get; set; }

    /// <summary>
    /// Writes this actability as the entry every packet that carries one uses.
    /// </summary>
    /// <remarks>
    /// Schema: <c>packed(id, point)</c> followed by <c>u8 step</c>. Id and point share one packed block,
    /// so the entry is not a fixed width and neither field can be written on its own - a reader takes the
    /// pair together. Point is published unsigned and is clamped at zero rather than wrapping.
    /// <para>
    /// This lives here rather than in a packet because more than one packet sends it, as a single entry
    /// and as a list element, and they have to agree: an entry written a field short is not skipped, it
    /// mis-frames everything after it in the body.
    /// </para>
    /// </remarks>
    public PacketStream Write(PacketStream stream)
    {
        stream.WritePisc(Id, (uint)Math.Max(0, Point));
        stream.Write(Step);
        return stream;
    }

    // Values for 1.2, not sure about the XP multiplier, might not have existed back then
    //                                                Rank    0      1      2      3      4      5      6      7
    //                                                 Exp    0      10k    20k    30k    40k    50k    70k    90k
    private static readonly float[] s_expMultipliers       = [1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f, 1.00f];
    private static readonly float[] s_laborCostMultipliers = [1.00f, 1.00f, 0.95f, 0.90f, 0.85f, 0.80f, 0.77f, 0.77f];
    private static readonly float[] s_timeMultipliers      = [1.00f, 1.00f, 0.95f, 0.90f, 0.85f, 0.80f, 0.77f, 0.77f];
    // TODO: Maybe apply the multipliers of the next step even when next rank isn't unlocked/upgraded yet, but you are at max xp for current rank 

    /*
    // These are 3.x values, and might not be correct for 1.2
    //                                                Rank    0      1      2      3      4      5      6      7      8      9      10     11
    private static readonly float[] s_expMultipliers       = [1.00f, 1.20f, 1.40f, 1.60f, 1.80f, 2.00f, 2.20f, 2.40f, 2.60f, 2.80f, 3.00f, 3.30f];
    private static readonly float[] s_laborCostMultipliers = [1.00f, 1.00f, 0.95f, 0.90f, 0.85f, 0.80f, 0.80f, 0.80f, 0.80f, 0.75f, 0.70f, 0.60f];
    private static readonly float[] s_timeMultipliers      = [1.00f, 0.97f, 0.94f, 0.94f, 0.94f, 0.88f, 0.88f, 0.88f, 0.84f, 0.84f, 0.80f, 0.74f];
    */

    /// <summary>
    /// Gets Exp multiplier for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetExpMultiplier() => s_expMultipliers[Math.Clamp(Step, 0, s_expMultipliers.Length - 1)];

    /// <summary>
    /// Gets Labor Cost *multiplier* for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetLaborCostMultiplier() => s_laborCostMultipliers[Math.Clamp(Step, 0, s_laborCostMultipliers.Length - 1)];

    /// <summary>
    /// Gets Production Time *multiplier* for the current skill level
    /// </summary>
    /// <returns></returns>
    public float GetProductionTimeMultiplier() => s_timeMultipliers[Math.Clamp(Step, 0, s_timeMultipliers.Length - 1)];

    /// <summary>
    /// Gets a multiplier to use for skill specific drops
    /// </summary>
    /// <returns></returns>
    public float GetLootMultiplier() => GetExpMultiplier();
}

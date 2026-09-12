namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// Durable state for one farmhand harvest job. The client keeps the static harvest id in the
/// job value and uses <see cref="JobId"/> as the database id when it requests cancellation.
/// Client serializer FUN_39aaac00 writes the two signed 16-bit counts, signed 64-bit world-time
/// update value, and u32 LP field.
/// </summary>
public sealed record ButlerHarvestJob(
    long JobId,
    uint StaticHarvestId,
    ushort RequestedAmount,
    ushort RemainingRepeatCount,
    uint LaborPowerForExperience,
    long UpdateTime);

/// <summary>A harvest job before MySQL assigns its durable database id.</summary>
public readonly record struct ButlerHarvestJobCandidate(
    uint StaticHarvestId,
    ushort RequestedAmount,
    ushort RemainingRepeatCount,
    uint LaborPowerForExperience,
    long UpdateTime);

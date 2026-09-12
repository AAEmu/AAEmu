namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// Durable state for one farmhand harvest job. The client keeps the static harvest id in the
/// job value and uses <see cref="JobId"/> as the database id when it requests cancellation.
/// Client serializer FUN_39aaac00 writes the two ushort counts, UTC update time, and u32 LP field.
/// </summary>
public sealed record ButlerHarvestJob(
    long JobId,
    uint StaticHarvestId,
    ushort RequestedAmount,
    ushort RemainingRepeatCount,
    uint LaborPowerForExperience,
    DateTime UpdateTimeUtc);

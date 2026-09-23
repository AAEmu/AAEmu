namespace AAEmu.Game.Models.Game.Music;

/// <summary>Where a performance's instrument comes from.</summary>
public enum InstrumentSource
{
    /// <summary>Nothing content calls an instrument was found.</summary>
    None = 0,

    /// <summary>The instrument item in the musical equipment slot.</summary>
    HeldItem = 1,

    /// <summary>The instrument doodad the player is attached to (the grand piano they sat down at).</summary>
    PlacedDoodad = 2,
}

/// <summary>What a play request may do.</summary>
public enum InstrumentPlayOutcome
{
    /// <summary>Refused: the only instrument in reach belongs to somebody else.</summary>
    RefusedNotYours = 0,

    /// <summary>Refused: no instrument in reach — content calls neither the attached doodad nor the equipped item one.</summary>
    RefusedNoInstrument = 1,

    /// <summary>Applied: the MIDI was announced and the instrument's buff applied (a buff id of 0 means content names none).</summary>
    Applied = 2,

    /// <summary>The instrument's buff is already on the unit: this play changes nothing, so nothing is applied a second time.</summary>
    AlreadyApplied = 3,
}

/// <summary>The decision a play request resolves to: what happened, through which source, carrying which buff.</summary>
public readonly record struct InstrumentPlayDecision(
    InstrumentPlayOutcome Outcome,
    InstrumentSource Source,
    uint BuffId);

/// <summary>
/// Decisions about playing through an instrument: which source wins, whether the player may play
/// through it, and whether the instrument's buff is already on them.
/// </summary>
/// <remarks>
/// The source and the buff both come from <see cref="GameData.InstrumentSoundGameData"/> — the
/// shipped <c>instrument_sounds</c> row — so nothing here knows a category, a name or a number.
/// A player who may not play through the placed instrument still falls back to an instrument of
/// their own; the refusal is only reached when nothing in reach is theirs to play.
/// </remarks>
public static class InstrumentPlayRules
{
    /// <param name="placedIsInstrument">Content names the attached doodad an instrument.</param>
    /// <param name="mayPlayPlaced">The player is allowed to play through that doodad (see <see cref="MusicInstrumentAccess"/>).</param>
    /// <param name="placedBuffId">The buff the placed instrument's row carries; 0 when it carries none.</param>
    /// <param name="placedBuffActive">That buff is already on the unit.</param>
    /// <param name="heldIsInstrument">Content names the equipped item an instrument.</param>
    /// <param name="heldBuffId">The buff the held instrument's row carries; 0 when it carries none.</param>
    /// <param name="heldBuffActive">That buff is already on the unit.</param>
    public static InstrumentPlayDecision Resolve(
        bool placedIsInstrument,
        bool mayPlayPlaced,
        uint placedBuffId,
        bool placedBuffActive,
        bool heldIsInstrument,
        uint heldBuffId,
        bool heldBuffActive)
    {
        if (placedIsInstrument && mayPlayPlaced)
        {
            return placedBuffId != 0 && placedBuffActive
                ? new InstrumentPlayDecision(InstrumentPlayOutcome.AlreadyApplied, InstrumentSource.PlacedDoodad,
                    placedBuffId)
                : new InstrumentPlayDecision(InstrumentPlayOutcome.Applied, InstrumentSource.PlacedDoodad,
                    placedBuffId);
        }

        if (heldIsInstrument)
        {
            return heldBuffId != 0 && heldBuffActive
                ? new InstrumentPlayDecision(InstrumentPlayOutcome.AlreadyApplied, InstrumentSource.HeldItem,
                    heldBuffId)
                : new InstrumentPlayDecision(InstrumentPlayOutcome.Applied, InstrumentSource.HeldItem, heldBuffId);
        }

        return placedIsInstrument
            ? new InstrumentPlayDecision(InstrumentPlayOutcome.RefusedNotYours, InstrumentSource.PlacedDoodad, 0u)
            : new InstrumentPlayDecision(InstrumentPlayOutcome.RefusedNoInstrument, InstrumentSource.None, 0u);
    }
}

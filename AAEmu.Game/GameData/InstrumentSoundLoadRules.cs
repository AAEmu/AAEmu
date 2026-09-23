using System.Text;

namespace AAEmu.Game.GameData;

/// <summary>
/// The one loud line <see cref="InstrumentSoundGameData"/> prints when <c>instrument_sounds</c>
/// carries rows this server cannot answer for. Kept as a pure builder so a test can pin what the
/// load reports instead of scraping the log.
/// </summary>
public static class InstrumentSoundLoadRules
{
    /// <param name="unknownKindRowIds">Row ids whose kind <c>enum_instrument_sound_kinds</c> does not name.</param>
    /// <param name="unknownBuffIds"><c>buff_id</c> values no <c>buffs</c> row carries.</param>
    public static string Warning(IReadOnlyCollection<uint> unknownKindRowIds, IReadOnlyCollection<uint> unknownBuffIds)
    {
        var message = new StringBuilder("instrument_sounds has rows this server can not answer for and dropped them:");
        if (unknownKindRowIds.Count > 0)
            message.Append($" {unknownKindRowIds.Count} row(s) with a kind enum_instrument_sound_kinds does not name (rows {string.Join(", ", unknownKindRowIds)});");
        if (unknownBuffIds.Count > 0)
            message.Append($" {unknownBuffIds.Count} buff id(s) no buffs row carries ({string.Join(", ", unknownBuffIds)});");
        message.Append(" their instruments are treated as non-instruments; no fallback value is used.");
        return message.ToString();
    }
}

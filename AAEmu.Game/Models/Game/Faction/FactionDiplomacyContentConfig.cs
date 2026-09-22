using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// The <c>content_configs</c> rows hero diplomacy reads (enum_content_configs ids 45, 46, 51, 142,
/// 288; all kind 27 "hero"). A missing row reads as 0 and closes the feature.
/// </summary>
public static class FactionDiplomacyContentConfig
{
    /// <summary>Id 45 = 3. Agreements one hero may conclude per day (ui_texts 10481: three per day, 10480: reset at midnight).</summary>
    public const string RequestCount = "faction_diplomacy_request_count";

    /// <summary>Id 46 = 3. Denials from one hero after which that hero takes no more requests from the same requester (ui_texts 10697, 10698).</summary>
    public const string DenyCount = "faction_diplomacy_deny_count";

    /// <summary>Id 51 = 50. History rows the client keeps; it drops the oldest past this (x2game-dev.dll 0x39cd1480).</summary>
    public const string HistorySize = "faction_diplomacy_history_size";

    /// <summary>Id 142 = 60. Minutes an agreement lasts: ui_texts 10473 and 10483 say one hour.</summary>
    public const string Term = "faction_diplomacy_term";

    /// <summary>Id 288 = 60. Seconds the accept dialog stays open; the client's own timer is 60 s (faction_relations.lua).</summary>
    public const string DialogTimeout = "faction_diplomacy_dialog_timeout";

    private static ContentConfigGameData Data => ContentConfigGameData.Instance;

    public static int RequestLimit => Data.GetInt(RequestCount, 0);

    public static int DenyLimit => Data.GetInt(DenyCount, 0);

    public static int HistoryLimit => Data.GetInt(HistorySize, 0);

    public static TimeSpan AgreementTerm => TimeSpan.FromMinutes(Data.GetInt(Term, 0));

    public static TimeSpan ProposalTimeout => TimeSpan.FromSeconds(Data.GetInt(DialogTimeout, 0));
}

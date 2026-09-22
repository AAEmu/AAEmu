using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Faction;

public class FactionRelation
{
    public FactionsEnum Id { get; set; }
    public FactionsEnum Id2 { get; set; }
    public RelationState State { get; set; }
    public DateTime ExpTime { get; set; }

    // Diplomacy overlay. All zero on a plain system_faction_relations row. Field names and widths
    // are the client's relation entry serializer (x2game-dev.dll 0x39398a90): type, type,
    // state(i8), nState(i8), updateTime(i64), changeTime(i64), type(u64) updater, updaterName,
    // type(u64) confirmer, confirmerName. The client sorts the two ids ascending on read.
    public RelationState NextState { get; set; }
    public DateTime UpdateTime { get; set; }
    public DateTime ChangeTime { get; set; }
    public uint UpdaterId { get; set; }
    public string UpdaterName { get; set; } = string.Empty;
    public uint ConfirmerId { get; set; }
    public string ConfirmerName { get; set; } = string.Empty;

    /// <summary>
    /// A row a hero agreement is overlaid on. The client keys "already has a relation" on a
    /// non-zero updater id (x2game-dev.dll 0x39ccd560), so a cleared row must zero it again.
    /// </summary>
    public bool HasDiplomacy => UpdaterId != 0;

    public void ClearDiplomacy()
    {
        NextState = 0;
        UpdateTime = DateTime.MinValue;
        ChangeTime = DateTime.MinValue;
        UpdaterId = 0;
        UpdaterName = string.Empty;
        ConfirmerId = 0;
        ConfirmerName = string.Empty;
    }
}

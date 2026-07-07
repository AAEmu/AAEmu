using AAEmu.Game.Models.Game.Crime;

namespace AAEmu.Game.Models.Game.Char;

public partial class Character
{
    public uint HostileFactionKills { get; set; }
    public uint HonorGainedInCombat { get; set; }
    public int ConsumedLaborPower { get; set; }
    public short DeadCount { get; set; }

    // Justice
    public int ArrestCount { get; set; }
    public int AcceptGuiltyCount { get; set; }
    public int AcceptTrialCount { get; set; }
    public int NotGuiltyCount { get; set; }
    public int GuiltyCount { get; set; }
    public int EvidenceReportedCount { get; set; }
    public int BotReportedCount { get; set; }
    public int ReportedAsBotCount { get; set; }
    /// <summary>When this is set at login, it means a trial was skipped and should be considered guilty and thrown into jail</summary>
    public int OfflineGuiltyTime { get; set; }
    public CourtRoomRegion OfflineGuiltyRegion { get; set; }
}

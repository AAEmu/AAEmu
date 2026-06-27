using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace AAEmu.Game.Models.Game;

public class JusticeConfig
{
    public bool KeepHistory { get; set; } = true;
    public bool AllowJuryEscape { get; set; } = true;
    public List<CourtRoomConfig> CourtRooms { get; init; } = [];
    public List<JailConfig> Jails { get; init; } = [];
    public int BotReportPrimeSuspectCount { get; set; } = 5;
}

public class CourtRoomConfig
{
    public uint Id { get; set; }
    public string Name { get; set; } = "Court Room";
    public FactionsEnum Faction { get; set; }
    public WorldSpawnPosition Defendant { get; set; }
    public WorldSpawnPosition HoldingCell { get; set; }
}

public class JailConfig
{
    public uint Id { get; set; }
    public string Name { get; set; } = "Jail";
    public FactionsEnum Faction { get; set; }
    public WorldSpawnPosition Pos { get; set; }
}

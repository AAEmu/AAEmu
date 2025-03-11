namespace AAEmu.Game.Models.Game.World.Zones;

public class IndunZone
{
    public uint ZoneGroupId { get; set; }
    public string Name { get; set; }
    public string Comment { get; set; }
    public uint LevelMin { get; set; } = 1;
    public uint LevelMax { get; set; } = 100;
    public uint MaxPlayers { get; set; } = 9999;
    public bool PvP { get; set; } = true;
    public bool HasGraveyard { get; set; } = true;
    public uint ItemId { get; set; }
    public uint RestoreItemTime { get; set; }
    public bool PartyOnly { get; set; }
    public bool ClientDriven { get; set; }
    public bool SelectChannel { get; set; }
    public string LocalizedName { get; set; }
    public uint VictoryTargetNpc { get; set; }
    public bool CanUseForceAttack { get; set; }
    public bool Duel { get; set; }
    public bool ExpPanelty { get; set; }
    public uint GearScore { get; set; }
    public uint EnterCount { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(LocalizedName) ? Name : LocalizedName;
    }
}

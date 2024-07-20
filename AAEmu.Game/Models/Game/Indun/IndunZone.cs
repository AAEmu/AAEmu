namespace AAEmu.Game.Models.Game.Indun
{
    public class IndunZone
    {
        public uint ZoneGroupId { get; set; }
        public uint LevelMin { get; set; }
        public uint LevelMax { get; set; }
        public uint MaxPlayers { get; set; }
        public bool PlayerCombat { get; set; }
        public bool HasGraveyard { get; set; }
        public uint ItemRequired { get; set; }
        public uint ItemCooldown { get; set; }
        public bool PartyRequired { get; set; }
        public bool ClientDriven { get; set; }
        public bool SelectChannel { get; set; }
    }
}

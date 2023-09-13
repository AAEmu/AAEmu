namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public class PassiveBuffTemplate
    {
        public uint Id { get; set; }
        public byte AbilityId { get; set; }
        public byte Level { get; set; }
        public uint BuffId { get; set; }
        public int ReqPoints { get; set; }
        public bool Active { get; set; }
    }
}
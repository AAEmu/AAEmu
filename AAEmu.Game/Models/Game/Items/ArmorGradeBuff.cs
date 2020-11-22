namespace AAEmu.Game.Models.Game.Items
{
    public class ArmorGradeBuff
    {
        public uint Id { get; set; }
        public ArmorType ArmorType { get; set; }
        public ItemGrade ItemGrade { get; set; }
        public uint BuffId { get; set; }
    }
}

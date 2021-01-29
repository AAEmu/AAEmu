namespace AAEmu.Game.Models.Game.Crafts
{
    /*
        Material required in a craft.
    */
    public class CraftMaterial
    {
        public uint Id {get; set;}
        public uint CraftId {get; set;}
        public uint ItemId {get; set;}
        public int Amount {get; set;}
        public bool MainGrade {get; set;}
        public int RequiredGrade { get; set; }
    }
}

namespace AAEmu.Game.Models.Game.Crafts
{
    /*
        Result of a craft
    */
    public class CraftProduct
    {
        public uint Id {get; set;}
        public uint CraftId {get; set;}
        public uint ItemId {get; set;}
        public int Amount {get; set;}
        public int Rate {get; set;}
        public bool ShowLowerCrafts {get; set;}
        public bool UseGrade {get; set;}
        public int ItemGradeId {get; set;}
    }
}

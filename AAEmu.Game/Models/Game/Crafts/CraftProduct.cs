namespace AAEmu.Game.Models.Game.Crafts;

/*
    Result of a craft
*/
public class CraftProduct
{
    public uint Id { get; set; }
    public uint CraftId { get; set; }
    public uint ItemId { get; set; }
    public int Amount { get; set; }
    public int Rate { get; set; }
    // 10.0.2.13: ShowLowerCrafts removed
    public bool UseGrade { get; set; }
    public uint ItemGradeId { get; set; }
}

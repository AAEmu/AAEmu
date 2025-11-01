namespace AAEmu.Game.Models.Game.Char.Templates;

public class ActabilityCategoriesTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint GroupId { get; set; }
    public bool VisibleUi { get; set; }
    public int VisibleOrder { get; set; }
}

namespace AAEmu.Game.Models.Game.DoodadObj.Templates;

public class DoodadCofferTemplate : DoodadTemplate
{
    public int Capacity { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsManikin { get; set; }
    public HashSet<int> AllowedItemCategoryIds { get; set; } = [];
}

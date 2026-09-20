namespace AAEmu.Game.Models.Game.Items.Templates;

/// <summary>
/// The request-sheet item: one template, and its instances carry the craft they stand for in their
/// own detail block.
/// </summary>
public class CraftOrderSheetTemplate : ItemTemplate
{
    public override Type ClassType => typeof(CraftOrderSheetItem);
}

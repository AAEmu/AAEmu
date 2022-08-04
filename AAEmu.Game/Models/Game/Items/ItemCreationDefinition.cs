namespace AAEmu.Game.Models.Game.Items;

public class ItemCreationDefinition
{
    public uint TemplateId { get; set; }
    public int Count { get; set; }
    public int GradeId { get; set; }

    public ItemCreationDefinition(uint templateId, int count = 1, int gradeId = -1)
    {
        TemplateId = templateId;
        Count = count;
        GradeId = gradeId;
    }
}

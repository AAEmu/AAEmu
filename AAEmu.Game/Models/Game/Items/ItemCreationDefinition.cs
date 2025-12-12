namespace AAEmu.Game.Models.Game.Items;

public class ItemCreationDefinition(uint templateId, int count = 1, int gradeId = -1)
{
    public uint TemplateId { get; set; } = templateId;
    public int Count { get; set; } = count;
    public int GradeId { get; set; } = gradeId;
}

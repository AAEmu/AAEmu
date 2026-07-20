namespace AAEmu.Game.Models.Game.Items.Templates;

public class RuneTemplate : ItemTemplate
{
    public override Type ClassType => typeof(Rune);

    public uint EquipSlotGroupId { get; set; }

    public bool IgnoreEquipItemTag { get; set; }

    public uint GemVisualEffectId { get; set; }

    public uint EquipItemTagId { get; set; }

    public uint EquipItemId { get; set; }

    public uint EisetId { get; set; }
    public byte EquipLevel { get; set; }
    public byte ItemGradeId { get; set; }
}
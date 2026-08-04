namespace AAEmu.Game.Models.Game.Items.Templates;

public class ArmorTemplate : EquipItemTemplate
{
    public override Type ClassType => typeof(Armor);

    public Wearable WearableTemplate { get; set; }
    public WearableKind KindTemplate { get; set; }
    public WearableSlot SlotTemplate { get; set; }
    public bool BaseEnchantable { get; set; }
    public bool BaseEquipment { get; set; }
    public uint AssetId { get; set; }
    public HashSet<uint> CompatibleModelIds { get; } = [];

    public bool HasCompatibleVisual(uint modelId)
    {
        // Assets without model-specific variants are either invisible or use their
        // generic default asset. Model-specific armor must match the actor model.
        return CompatibleModelIds.Count == 0 || CompatibleModelIds.Contains(modelId);
    }
}

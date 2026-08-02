namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootActabilityGroups
{
    // 10.0.2.13 loot_actability_groups has no surrogate id; key is (loot_pack_id, loot_group_id).
    public uint LootPackId { get; set; }
    public uint GroupId { get; set; }
    public uint MaxDice { get; set; }
    public uint MinDice { get; set; }
}

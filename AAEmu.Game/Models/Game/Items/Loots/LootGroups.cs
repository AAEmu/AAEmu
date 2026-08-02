namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootGroups : IComparable<LootGroups>
{
    // 10.0.2.13 loot_groups has no surrogate id; key is (pack_id, group_no).
    public uint PackId { get; set; }
    public uint GroupNo { get; set; }
    public uint DropRate { get; set; }
    public byte ItemGradeDistributionId { get; set; }
    public uint ZoneGroupId { get; set; }

    public int CompareTo(LootGroups other)
    {
        var pack = PackId.CompareTo(other.PackId);
        return pack != 0 ? pack : GroupNo.CompareTo(other.GroupNo);
    }
}

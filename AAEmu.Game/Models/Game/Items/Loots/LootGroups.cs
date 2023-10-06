using System;

namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootGroups : IComparable<LootGroups>
{
    public uint Id { get; set; }
    public uint PackId { get; set; }
    public uint GroupNo { get; set; }
    public uint DropRate { get; set; }
    public byte ItemGradeDistributionId { get; set; }

    /// <summary>
    /// To sort an array 
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(LootGroups other)
    {
        return Id.CompareTo(other.Id);
    }
}

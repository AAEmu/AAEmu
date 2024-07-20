using System;

namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootPackConvertFish : IComparable<LootPackConvertFish>
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public uint LootPackId { get; set; }
    public uint DoodadFuncConvertFishId { get; set; }

    /// <summary>
    /// To sort an array
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public int CompareTo(LootPackConvertFish other)
    {
        return Id.CompareTo(other.Id);
    }
}

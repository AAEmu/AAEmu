namespace AAEmu.Game.Models.Game.Items.Loots;

public class LootPackConvertFish : IComparable<LootPackConvertFish>
{
    public uint Id { get; set; }
    public uint ItemId { get; set; }
    public uint ConvertItemId { get; set; } // 10.0.2.13: convert is now a direct item, not a loot-pack roll
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

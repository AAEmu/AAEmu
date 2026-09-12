using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// The nested Butler state emitted by the 10.0.2.13 client serializer <c>FUN_39CD7A80</c>.
/// Collection lengths and the equipment valid-flags word are derived when written so callers
/// cannot publish mismatched metadata.
/// </summary>
public sealed record ButlerInfoWire(
    ulong OwnerId,
    sbyte WorldId,
    string Name,
    ushort HouseTlId,
    uint LaborPower,
    ushort LpChargedAmount,
    ushort RemainProductionCost,
    IReadOnlyDictionary<int, Item> Equipment,
    IReadOnlyList<Item> BagItems,
    IReadOnlyDictionary<sbyte, ulong> PermanentDatas,
    IReadOnlyList<ButlerActabilityWire> Actabilities,
    IReadOnlyDictionary<long, ButlerHarvestDataWire> HarvestDatas,
    IReadOnlyDictionary<long, ButlerSpecialtyTradeDataWire> SpecialtyTradeDatas,
    IReadOnlyDictionary<uint, uint> UnitAttributes)
{
    /// <summary>
    /// Builds the exact zero-collection form accepted by <c>FUN_39CD7A80</c>. Scalar values are
    /// supplied by the authoritative character and bound-house state; this factory does not infer them.
    /// </summary>
    public static ButlerInfoWire Empty(
        ulong ownerId,
        sbyte worldId,
        string name,
        ushort houseTlId,
        uint laborPower,
        ushort lpChargedAmount,
        ushort remainProductionCost) => new(
        ownerId,
        worldId,
        name,
        houseTlId,
        laborPower,
        lpChargedAmount,
        remainProductionCost,
        new Dictionary<int, Item>(),
        Array.Empty<Item>(),
        new Dictionary<sbyte, ulong>(),
        Array.Empty<ButlerActabilityWire>(),
        new Dictionary<long, ButlerHarvestDataWire>(),
        new Dictionary<long, ButlerSpecialtyTradeDataWire>(),
        new Dictionary<uint, uint>());

    public void Write(PacketStream stream)
    {
        stream.Write(OwnerId);
        stream.Write(WorldId);
        stream.Write(Name);
        stream.Write(HouseTlId);
        stream.Write(LaborPower);
        stream.Write(LpChargedAmount);
        stream.Write(RemainProductionCost);

        EquipmentSerializer.WriteButler(stream, Equipment);

        stream.Write(BagItems.Count);
        foreach (var item in BagItems)
            item.Write(stream);

        WritePermanentDatas(stream, PermanentDatas);
        WriteActabilities(stream, Actabilities);

        stream.Write(HarvestDatas.Count);
        foreach (var (key, data) in HarvestDatas.OrderBy(entry => entry.Key))
        {
            stream.Write(key);
            data.Write(stream);
        }

        stream.Write(SpecialtyTradeDatas.Count);
        foreach (var (key, data) in SpecialtyTradeDatas.OrderBy(entry => entry.Key))
        {
            stream.Write(key);
            data.Write(stream);
        }

        WriteUnitAttributes(stream, UnitAttributes);
    }

    internal static void WritePermanentDatas(PacketStream stream, IReadOnlyDictionary<sbyte, ulong> permanentDatas)
    {
        stream.Write(permanentDatas.Count);
        foreach (var (key, value) in permanentDatas.OrderBy(entry => entry.Key))
        {
            stream.Write(key);
            stream.Write(value);
        }
    }

    internal static void WriteActabilities(PacketStream stream, IReadOnlyList<ButlerActabilityWire> actabilities)
    {
        stream.Write(actabilities.Count);
        foreach (var actability in actabilities)
        {
            stream.WritePisc(actability.GroupId, actability.StatId);
            stream.Write(actability.Point);
        }
    }

    internal static void WriteUnitAttributes(PacketStream stream, IReadOnlyDictionary<uint, uint> unitAttributes)
    {
        stream.Write(unitAttributes.Count);
        foreach (var (key, value) in unitAttributes.OrderBy(entry => entry.Key))
        {
            stream.Write(key);
            stream.Write(value);
        }
    }
}

/// <summary>
/// Farmhand actability payload in <c>FUN_39AA47B0</c>: PISC group/stat, then a signed 16-bit point value.
/// </summary>
public readonly record struct ButlerActabilityWire(uint GroupId, uint StatId, short Point);

/// <summary>
/// Value following each raw signed 64-bit harvest-data key in <c>FUN_39AAAC00</c>.
/// </summary>
public readonly record struct ButlerHarvestDataWire(
    uint HarvestId,
    short RepeatCount,
    short RequestedAmount,
    uint LpForCalcExp,
    long UpdateTime)
{
    internal void Write(PacketStream stream)
    {
        stream.Write(HarvestId);
        stream.Write(RepeatCount);
        stream.Write(RequestedAmount);
        stream.Write(LpForCalcExp);
        stream.Write(UpdateTime);
    }
}

/// <summary>
/// Value following each raw signed 64-bit specialty-trade-data key in <c>FUN_39AAACA0</c>.
/// </summary>
public readonly record struct ButlerSpecialtyTradeDataWire(
    uint SpecialtyType,
    short ToZoneGroupType,
    long CreatedTime,
    uint DeliveryTime)
{
    internal void Write(PacketStream stream)
    {
        stream.Write(SpecialtyType);
        stream.Write(ToZoneGroupType);
        stream.Write(CreatedTime);
        stream.Write(DeliveryTime);
    }
}

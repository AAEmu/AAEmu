using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Butlers;

/// <summary>
/// The nested Butler state the 10.0.2.13 client's Butler state serializer writes and reads back.
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
    /// Builds the exact zero-collection form the client's Butler state serializer accepts. Scalar values are
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
            stream.WritePisc(actability.GroupId, actability.Point);
            stream.Write(actability.Stat);
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
/// Farmhand actability payload: PISC group/point, then signed 16-bit stat. The group key and
/// the distinct point/stat values are read straight from the client's own actability
/// serialization rather than inferred from the field widths.
/// </summary>
public readonly record struct ButlerActabilityWire(uint GroupId, uint Point, short Stat);

/// <summary>
/// Value following each raw signed 64-bit harvest-data key in the client's harvest-data
/// serializer.
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
/// Value following each raw signed 64-bit specialty-trade-data key in the client's
/// specialty-trade serializer.
/// </summary>
public readonly record struct ButlerSpecialtyTradeDataWire(
    uint SpecialtyType,
    ushort ToZoneGroupType,
    ulong CreatedTime,
    int DeliveryTime)
{
    internal void Write(PacketStream stream)
    {
        stream.Write(SpecialtyType);
        stream.Write(ToZoneGroupType);
        stream.Write(CreatedTime);
        stream.Write(DeliveryTime);
    }
}

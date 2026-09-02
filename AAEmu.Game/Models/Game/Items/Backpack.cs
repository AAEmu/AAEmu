using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class Backpack : Item
{
    public DateTime FreshnessStartTime =>
        TryGetFreshness(out var freshnessStartTime, out _) ? freshnessStartTime : DateTime.MinValue;

    public ushort ProductionZoneGroupId =>
        TryGetFreshness(out _, out var productionZoneGroupId) ? productionZoneGroupId : (ushort)0;

    public Backpack()
    {
    }

    public Backpack(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public void InitializeFreshness(DateTime freshnessStartTime, ushort productionZoneGroupId)
    {
        if (DetailType != ItemDetailType.Invalid || Detail is { Length: > 0 })
            throw new InvalidOperationException($"Backpack item {Id} already has item detail data.");
        if (freshnessStartTime <= DateTime.UnixEpoch)
            throw new ArgumentOutOfRangeException(
                nameof(freshnessStartTime),
                freshnessStartTime,
                "Freshness start time must be after the Unix epoch.");
        if (freshnessStartTime.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Freshness start time must be UTC.", nameof(freshnessStartTime));
        if (productionZoneGroupId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(productionZoneGroupId),
                productionZoneGroupId,
                "Production zone group must be nonzero.");

        var detail = new PacketStream();
        detail.Write(freshnessStartTime);
        detail.Write(productionZoneGroupId);
        DetailType = ItemDetailType.BackpackFreshness;
        Detail = detail.GetBytes();
    }

    public bool TryGetFreshness(out DateTime freshnessStartTime, out ushort productionZoneGroupId)
    {
        freshnessStartTime = DateTime.MinValue;
        productionZoneGroupId = 0;
        if (DetailType != ItemDetailType.BackpackFreshness || Detail is not { Length: 10 })
            return false;

        var detail = (PacketStream)Detail;
        freshnessStartTime = detail.ReadDateTime();
        productionZoneGroupId = detail.ReadUInt16();
        return freshnessStartTime > DateTime.UnixEpoch && productionZoneGroupId != 0;
    }
}

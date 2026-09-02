using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Trading;

public enum SpecialtyPackProductionSource
{
    Craft = 1,
    DoodadLootItem = 2,
    DoodadLootPack = 3
}

public readonly record struct SpecialtyPackProductionContext(
    SpecialtyPackProductionSource Source,
    DateTime FreshnessStartTime,
    uint ProductionZoneGroupId,
    uint ProducerId)
{
    public bool IsValid =>
        Enum.IsDefined(Source) &&
        FreshnessStartTime.Kind == DateTimeKind.Utc &&
        FreshnessStartTime > DateTime.UnixEpoch &&
        ProductionZoneGroupId is > 0 and <= ushort.MaxValue &&
        (Source != SpecialtyPackProductionSource.Craft || ProducerId != 0);
}

public static class SpecialtyPackMaterializer
{
    public static bool RequiresProductionContext(ItemTemplate template) =>
        template is BackpackTemplate
        {
            BackpackType: BackpackType.TradePack,
            FreshnessGroupId: > 0
        };

    public static bool CanMaterialize(
        ItemTemplate template,
        SpecialtyPackProductionContext? productionContext)
    {
        if (!RequiresProductionContext(template))
            return true;

        return productionContext is { IsValid: true } context &&
               (template.SpecialtyZoneId == 0 || template.SpecialtyZoneId == context.ProductionZoneGroupId);
    }

    public static bool TryMaterialize(
        Item item,
        SpecialtyPackProductionContext? productionContext)
    {
        if (!RequiresProductionContext(item.Template))
            return true;
        if (!CanMaterialize(item.Template, productionContext) ||
            productionContext is not { } context ||
            item is not Backpack backpack ||
            !item.HasDefaultDetail)
            return false;

        item.MadeUnitId = context.ProducerId;
        backpack.InitializeFreshness(
            context.FreshnessStartTime,
            checked((ushort)context.ProductionZoneGroupId));
        return true;
    }
}

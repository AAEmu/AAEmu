using AAEmu.Game.Models.Game.Crafts;

namespace AAEmu.Game.Core.Managers;

public interface ICraftManager : ILoadable
{
    bool TryGetCraft(uint craftId, out Craft craft);
    bool IsCraftInPack(uint craftPackId, uint craftId);
    IReadOnlyCollection<uint> GetCraftIdsForPack(uint craftPackId);

    bool TryGetCraftLine(uint craftLineId, out CraftLine craftLine);
    IReadOnlyCollection<uint> GetCraftIdsForLine(uint craftLineId);

    bool TryGetCraftCategory(CraftCategoryLevel level, uint categoryId, out CraftCategory category);
    IReadOnlyCollection<uint> GetCraftIdsForCategory(CraftCategoryLevel level, uint categoryId);

    bool TryGetCraftPack(uint craftPackId, out CraftPack craftPack);
    IReadOnlyCollection<uint> GetUnresolvedCraftPackIds();
    IReadOnlyCollection<uint> GetUnresolvedProductPackIds();
    IReadOnlyCollection<CraftCategoryMismatch> GetCraftCategoryMismatches();
}

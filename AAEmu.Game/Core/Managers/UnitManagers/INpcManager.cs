using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public interface INpcManager : ILoadable, IInitializable
{
    bool Exist(uint templateId);
    NpcTemplate GetTemplate(uint templateId);
    Dictionary<uint, NpcTemplate> GetAllTemplates();
    MerchantGoods GetGoods(uint id);
    IReadOnlyDictionary<uint, MerchantPurchaseState> GetMerchantPurchaseStates(uint characterId);
    bool TryReserveMerchantPurchases(
        uint characterId,
        IEnumerable<(MerchantGoodsItem Good, int Count)> purchases,
        out MerchantGoodsItem failedGood,
        out IReadOnlyDictionary<uint, MerchantPurchaseState> updatedStates);
    bool TryRollbackMerchantPurchases(
        uint characterId,
        IEnumerable<(MerchantGoodsItem Good, int Count)> purchases);
    void ResetMerchantPurchases(MerchantPurchaseType purchaseType);
    Npc Create(WorldInstance parentWorld, uint objectId, uint templateId);
    void BindSkillsToTemplate(uint templateId, List<NpcSkill> skills);
}

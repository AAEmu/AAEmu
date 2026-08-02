using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Merchant;

namespace AAEmu.Game.Models.Tasks.Merchant;

public class MerchantPurchaseResetTask(MerchantPurchaseType purchaseType) : Task
{
    public override void Execute()
    {
        NpcManager.Instance.ResetMerchantPurchases(purchaseType);
    }
}

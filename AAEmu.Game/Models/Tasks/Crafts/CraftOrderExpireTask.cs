using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Crafts;

/// <summary>Sweeps listings that have reached their coupon-band lifetime.</summary>
public class CraftOrderExpireTask : Task
{
    public override void Execute()
    {
        CraftOrderManager.Instance.SweepExpired(DateTimeOffset.UtcNow);
    }
}

using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Justice;

/// <summary>
/// The imprison-or-trial offer was not answered in time, so the courthouse state it was holding comes
/// off again. A reply that arrives first clears the offer, which makes this a no-op.
/// </summary>
public class ImprisonOrTrialOfferTimeoutTask(uint characterId) : Task
{
    public override void Execute()
    {
        JusticeManager.Instance.ExpireImprisonOrTrialOffer(characterId);
    }
}

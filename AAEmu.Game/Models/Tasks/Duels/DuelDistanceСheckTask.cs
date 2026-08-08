using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelDistanceСheckTask(Duel duel) : Task
{
    protected Duel _duel = duel;
    protected uint _challengerId = duel.Challenger.Id;
    protected uint _challengedId = duel.Challenged.Id;

    public override void Execute()
    {
        if (_duel.DuelDistanceСheckTask == null)
            return;

        var res = DuelManager.Instance.DuelDistanceСheck(_challengerId);
        switch (res)
        {
            case DuelDistance.ChallengerFar:
                DuelManager.Instance.DuelStop(_challengedId, DuelDetType.Cancel, _challengerId);
                break;
            case DuelDistance.ChallengedFar:
                DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Cancel, _challengedId);
                break;
            case DuelDistance.Error:
                break;
            case DuelDistance.Near:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

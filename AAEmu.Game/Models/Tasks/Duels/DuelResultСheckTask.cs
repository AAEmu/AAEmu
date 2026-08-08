using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelResultСheckTask(Duel duel) : Task
{
    protected Duel _duel = duel;
    protected uint _challengerId = duel.Challenger.Id;
    protected uint _challengedId = duel.Challenged.Id;

    public override void Execute()
    {
        if (_duel.DuelResultСheckTask == null)
            return;

        var res = DuelManager.Instance.DuelResultСheck(_challengerId);
        if (res)
        {
            if (_duel.Challenger.Hp <= 1)
            {
                DuelManager.Instance.DuelStop(_challengedId, DuelDetType.Decided, _challengerId);
            }
            else if (_duel.Challenged.Hp <= 1)
            {
                DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Decided, _challengedId);
            }
            else if (_duel.Challenger.Hp <= 1 && _duel.Challenged.Hp <= 1)
            {
                DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Draw, _challengedId);
            }
        }
    }
}

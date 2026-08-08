using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelEndTimerTask(Duel duel, uint challengerId) : Task
{
    protected Duel _duel = duel;
    protected uint _challengerId = challengerId;

    public override async System.Threading.Tasks.Task ExecuteAsync()
    {
        if (_duel.DuelEndTimerTask == null)
            return;

        await _duel.DuelEndTimerTask.CancelAsync();
        InternalExecute();
    }

    public override void Execute()
    {
        if (_duel.DuelEndTimerTask == null)
            return;

        _duel.DuelEndTimerTask.Cancel();
        InternalExecute();
    }

    private void InternalExecute()
    {
        _duel.DuelEndTimerTask = null;

        if (_duel.Challenger.Hp < _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_duel.Challenged.Id, DuelDetType.Decided, _challengerId);
        }
        else if (_duel.Challenger.Hp > _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Decided, _duel.Challenged.Id);
        }
        else if (_duel.Challenger.Hp == _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Draw, _duel.Challenged.Id);
        }
    }
}

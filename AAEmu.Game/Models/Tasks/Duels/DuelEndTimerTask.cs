using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Duels;

namespace AAEmu.Game.Models.Tasks.Duels;

public class DuelEndTimerTask : Task
{
    protected Duel _duel;
    protected uint _challengerId;

    public DuelEndTimerTask(Duel duel, uint challengerId)
    {
        _duel = duel;
        _challengerId = challengerId;
    }

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
            DuelManager.Instance.DuelStop(_duel.Challenged.Id, DuelDetType.Win, _challengerId);
        }
        else if (_duel.Challenger.Hp > _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Win, _duel.Challenged.Id);
        }
        else if (_duel.Challenger.Hp == _duel.Challenged.Hp)
        {
            DuelManager.Instance.DuelStop(_challengerId, DuelDetType.Draw, _duel.Challenged.Id);
        }
    }
}

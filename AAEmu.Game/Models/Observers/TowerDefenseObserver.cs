using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.TowerDefs;
using NLog;

namespace AAEmu.Game.Models.Observers;

public class TowerDefenseObserver : IObserver<float>
{
    private readonly TowerDef _owner;
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _running;

    public TowerDefenseObserver(TowerDef owner)
    {
        _owner = owner;
        // skip first
        _running = true;
    }

    public void OnCompleted()
    {
        throw new NotImplementedException();
    }

    public void OnError(Exception error)
    {
        throw new NotImplementedException();
    }

    public void OnNext(float value)
    {
        if (_running)
        {
            if (value < _owner.TimeOfDay)
            {
                _running = false;
            }

            return;
        }

        if (!(value >= _owner.TimeOfDay) || _running)
        {
            return;
        }

        _running = true;
        TowerDefenseManager.Instance.start(_owner);
    }
}

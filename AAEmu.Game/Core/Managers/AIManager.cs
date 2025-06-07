using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.v2.Framework;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AIManager : Singleton<AIManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _initialized = false;

    private List<NpcAi> _npcAis;
    private object _aiLock;

    public void Initialize()
    {
        if (_initialized)
            return;

        _npcAis = [];
        _aiLock = new object();
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100), true);

        _initialized = true;
    }

    public void AddAi(NpcAi ai)
    {
        lock (_aiLock)
        {
            _npcAis.Add(ai);
        }
    }

    public void Tick(TimeSpan delta)
    {
        lock (_aiLock)
        {
            foreach (var npcai in _npcAis.ToList())
            {
                try
                {
                    if (npcai.Owner != null)
                        npcai.Tick(delta);
                }
                catch (Exception e)
                {
                    Logger.Error(e);
                }
            }
        }
    }

    public void Stop()
    {
        Logger.Debug($"Stopping AIManager");
        TickManager.Instance.OnTick.UnSubscribe(Tick);
    }
}

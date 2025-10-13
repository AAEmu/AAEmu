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
        if (_npcAis.Count == 0)
            return;

        var now = DateTime.UtcNow;
        // Create a copy of the list to avoid collection modification during iteration
        var npcsToTick = new List<NpcAi>(_npcAis);
        foreach (var npcai in npcsToTick)
        {
            // Remove AI instances with null Owner from the list immediately
            if (npcai.Owner == null || npcai.ShouldTick == false)
            {
                lock (_npcAis)
                {
                    if (_npcAis.Contains(npcai))
                        _npcAis.Remove(npcai);
                }
                continue;
            }
            
            try
            {
                npcai.Tick(delta);
            }
            catch (Exception ex)
            {
                Logger.Error("AIManager - " + ex.ToString());
                // Optionally remove the problematic AI to prevent repeated errors
                lock (_npcAis)
                {
                    if (_npcAis.Contains(npcai))
                        _npcAis.Remove(npcai);
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

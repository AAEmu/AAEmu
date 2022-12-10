using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.v2;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AIManager : Singleton<AIManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _initialized = false;

        private List<NpcAi> _npcAis;
        private object _aiLock;
        
        public void Initialize()
        {
            if (_initialized)
                return;

            _npcAis = new List<NpcAi>();
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
                        npcai.Tick(delta);
                    }
                    catch (Exception e)
                    {
                        _log.Error(e);
                    }
                }
            }
        }
    }
}

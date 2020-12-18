using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.v2;

namespace AAEmu.Game.Core.Managers
{
    public class AIManager : Singleton<AIManager>
    {
        private List<NpcAi> _npcAis;
        private object _aiLock;
        
        public void Initialize()
        {
            _npcAis = new List<NpcAi>();
            _aiLock = new object();
            TickManager.Instance.OnHighFrequencyTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
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
                    npcai.Tick(delta);
                }
            }
        }
    }
}

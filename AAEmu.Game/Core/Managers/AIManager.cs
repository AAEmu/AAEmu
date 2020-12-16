using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Core.Managers
{
    public class AIManager : Singleton<AIManager>
    {
        public List<AbstractAI> ActiveAIs;
        private object _aiLock;
        
        public void Initialize()
        {
            ActiveAIs = new List<AbstractAI>();
            _aiLock = new object();
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
        }

        public void AddAI(AbstractAI AI)
        {
            lock (_aiLock)
            {
                ActiveAIs.Add(AI);
            }
        }

        public void Tick(TimeSpan delta)
        {
            lock (_aiLock)
            {
                foreach (var AI in ActiveAIs.ToList())
                {
                    AI.StateMachine.Tick(delta);
                }
            }
        }
    }
}

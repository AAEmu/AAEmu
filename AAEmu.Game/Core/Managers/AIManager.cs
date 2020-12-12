using System;
using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Core.Managers
{
    public class AIManager : Singleton<AIManager>
    {
        public List<AbstractAI> ActiveAIs;
        
        public void Initialize()
        {
            ActiveAIs = new List<AbstractAI>();
            TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(100));
        }

        public void AddAI(AbstractAI AI)
        {
            ActiveAIs.Add(AI);
        }

        public void Tick(TimeSpan delta)
        {
            foreach (var AI in ActiveAIs)
            {
                AI.StateMachine.Tick(delta);
            }
        }
    }
}

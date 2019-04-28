using System;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    ///模拟线路 / Analog circuit
    ///这里的模拟线路指由开发人员进行手动收集所行走的路线然后保存。 / Analog lines here refer to routes that are collected manually by developers and then saved.
    ///控制NPC按照这种路线进行移动 / Control NPC to move along this route
    /// </summary>
    public class Simulation : Patrol
    {
        public override void Execute(Npc npc)
        {
            throw new NotImplementedException();
        }
    }
}

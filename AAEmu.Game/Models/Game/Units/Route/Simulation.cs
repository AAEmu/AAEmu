using System;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    ///模拟线路
    ///这里的模拟线路指由开发人员进行手动收集所行走的路线然后保存。
    ///控制NPC按照这种路线进行移动
    /// </summary>
    public class Simulation : Patrol
    {
        public override void Execute(Npc npc)
        {
            throw new NotImplementedException();
        }
    }
}

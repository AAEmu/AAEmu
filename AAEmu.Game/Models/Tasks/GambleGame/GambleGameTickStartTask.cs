//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace AAEmu.Game.Models.Tasks.GambleGame
//{
//    class GambleGameTickStartTask
//    {
//    }
//}


using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.GambleGame
{
    public class GambleGameTickStartTask : Task
    {
        public GambleGameTickStartTask()
        {
        }

        public override void Execute()
        {
            GambleGameManager.Instance.GambleGameTick();
        }
    }
}

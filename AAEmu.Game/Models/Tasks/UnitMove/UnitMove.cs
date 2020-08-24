using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using SQLitePCL;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class UnitMove : Task
    {
        private readonly Patrol _patrol;
        private readonly BaseUnit _unit;

        /// <summary>
        /// 初始化任务
        /// Initialization task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="npc"></param>
        public UnitMove(Patrol patrol, BaseUnit unit)
        {
            _patrol = patrol;
            _unit = unit;
        }

        /// <summary>
        /// 执行任务
        /// Perform tasks
        /// </summary>
        public override void Execute()
        {
            if (_unit is Npc npc)
            {
                _patrol?.Apply(npc);
            }

            if (_unit is Gimmick gimmick)
            {
                _patrol?.Apply(gimmick);
            }

            if (_unit is Transfer transfer)
            {
                _patrol?.Apply(transfer);
            }

        }
    }
}

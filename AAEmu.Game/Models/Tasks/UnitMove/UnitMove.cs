using System;
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
        /// <param name="unit"></param>
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
            switch (_unit)
            {
                case Npc npc:
                    _patrol?.Apply(npc);
                    break;
                case Gimmick gimmick:
                    _patrol?.Apply(gimmick);
                    break;
                case Transfer transfer:
                    _patrol?.Apply(transfer);
                    break;
                default:
                    _patrol?.Apply(_unit);
                    break;
            }
        }
    }
}

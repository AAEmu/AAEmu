using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class UnitMovePause : Task
    {
        private readonly Patrol _patrol;
        private readonly BaseUnit _unit;

        /// <summary>
        /// 初始化任务
        /// Initialization task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="unit"></param>
        public UnitMovePause(Patrol patrol, BaseUnit unit)
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
                case Npc _npc:
                    if (_npc.Hp > 0)
                        _patrol?.LoopAuto(_npc);
                    break;
                case Gimmick _gimmick:
                    _patrol?.LoopAuto(_gimmick);
                    break;
                case Transfer _transfer:
                    _patrol?.LoopAuto(_transfer);
                    break;
            }
        }
    }
}

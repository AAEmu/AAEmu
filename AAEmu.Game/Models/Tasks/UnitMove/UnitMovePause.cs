using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class UnitMovePause : Task
    {
        private readonly Patrol _patrol;
        private readonly Npc _npc;

        /// <summary>
        /// 初始化任务
        /// Initialization task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="npc"></param>
        public UnitMovePause(Patrol patrol, Npc npc)
        {
            _patrol = patrol;
            _npc = npc;
        }

        /// <summary>
        /// 执行任务
        /// Perform tasks
        /// </summary>
        public override void Execute()
        {
            if (_npc.Hp > 0)
            {
                _patrol?.LoopAuto(_npc);
            }
        }
    }
}

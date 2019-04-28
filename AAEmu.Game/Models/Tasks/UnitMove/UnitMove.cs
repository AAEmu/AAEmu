using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class UnitMove : Task
    {
        private readonly Patrol _patrol;
        private readonly Npc _npc;
        /// <summary>
        /// 初始化任务
        /// </summary>
        /// <param name="caster"></param>
        public UnitMove(Patrol patrol, Npc npc)
        {
            _patrol = patrol;
            _npc = npc;
        }
        /// <summary>
        /// 执行任务
        /// </summary>
        public override void Execute()
        {
            if(_npc.Hp>0)
                _patrol.Apply(_npc);
        }
    }
}

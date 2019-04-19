﻿using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class UnitMove : Task
    {
        private readonly Patrol _patrol;
        private readonly Npc _targetId;
        /// <summary>
        /// 初始化任务
        /// </summary>
        /// <param name="caster"></param>
        public UnitMove(Patrol patrol, Npc npc)
        {
            _patrol = patrol;
            _targetId = npc;
        }
        /// <summary>
        /// 执行任务
        /// </summary>
        public override void Execute()
        {
            _patrol.Apply(_targetId);
        }
    }
}

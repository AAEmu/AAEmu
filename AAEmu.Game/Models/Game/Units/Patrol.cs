using System;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units
{
    /// <summary>
    /// Unit 巡逻类
    /// </summary>
    public abstract class Patrol
    {
        /// <summary>
        /// 是否正在执行巡逻任务
        /// 默认为 False
        /// </summary>
        public bool Running { get; set; } = false;
        /// <summary>
        /// 是否为循环
        /// 默认为 True
        /// </summary>
        public bool Loop { get; set; } = true;
        /// <summary>
        /// 循环间隔 毫秒
        /// </summary>
        public uint LoopDelay { get; set; }
        /// <summary>
        /// 执行进度 0-100
        /// </summary>
        public sbyte Step { get; set; }
        /// <summary>
        /// 中断 True
        /// 如被攻击或其他行为改变自身状态等 是否终止路线
        /// </summary>
        public bool Interrupt { get; set; } = true;

        /// <summary>
        /// 执行巡逻任务
        /// </summary>
        /// <param name="caster"></param>
        public void Apply(Unit caster, Npc npc)
        {
            Execute(caster,npc);
        }

        public abstract void Execute(Unit caster, Npc npc);
    }
}

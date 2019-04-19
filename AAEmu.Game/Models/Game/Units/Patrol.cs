using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;

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
        public bool Running { get; set; } = true;
        /// <summary>
        /// 是否为循环
        /// 默认为 True
        /// </summary>
        public bool Loop { get; set; } = true;
        /// <summary>
        /// 循环间隔 毫秒
        /// </summary>
        public double LoopDelay { get; set; }
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
        /// 执行顺序编号
        /// 每次执行必须递增序号，否则重复序号的动作不被执行
        /// </summary>
        public uint Seq { get; set; } = 0;
        /// <summary>
        /// 当前执行次数
        /// </summary>
        protected uint Count { get; set; } = 0;
        /// <summary>
        /// 暂停巡航点
        /// </summary>
        protected Point PausePosition { get; set; }




        /// <summary>
        /// 执行巡逻任务
        /// </summary>
        /// <param name="caster"></param>
        public void Apply(Npc npc)
        {
            //如果NPC不存在或不处于巡航模式或者当前执行次数不为0
            if (npc.Patrol == null || (npc.Patrol.Running == false && this!=npc.Patrol) ||(npc.Patrol.Running == true && this==npc.Patrol))
            {
                ++Count;
                ++Seq;
                Running = true;
                npc.Patrol = this;
                Execute(npc);
            }
        }

        public abstract void Execute(Npc npc);
        /// <summary>
        /// 再次执行任务
        /// </summary>
        /// <param name="npc"></param>
        public void Repet(Npc npc,double time = 100)
        {
            TaskManager.Instance.Schedule(new UnitMove(this, npc), TimeSpan.FromMilliseconds(time));
        }
        public void PauseAuto(Npc npc)
        {
            if (Interrupt)
            {
                Pause(npc);
            }
        }
        public void Pause(Npc npc)
        {
            Running = false;
        }

        public void Stop(Npc npc)
        {
            Pause(npc);
        }
        public void Recovery(Npc npc)
        {
            if (PausePosition!=null)
            {
                Running = true;
                Repet(npc);
            }

        }
        public void LoopAuto(Npc npc)
        {
            if (Loop)
            {
                Count = 0;
                Seq = 0;
                Repet(npc,LoopDelay);
            }
        }
    }
}

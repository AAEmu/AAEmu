using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Route;
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
        /// 上次任务
        /// </summary>
        public Patrol LastPatrol { get; set; }
        /// <summary>
        /// 放弃任务
        /// </summary>
        public bool Abandon { get; set; } = false;




        /// <summary>
        /// 执行巡逻任务
        /// </summary>
        /// <param name="caster"></param>
        public void Apply(Npc npc)
        {
            //如果NPC不存在或不处于巡航模式或者当前执行次数不为0
            if (npc.Patrol == null || (npc.Patrol.Running == false && this!=npc.Patrol) ||(npc.Patrol.Running == true && this==npc.Patrol))
            {
                //如果上次巡航模式处于暂停状态则保存上次巡航模式
                if (npc.Patrol!=null && npc.Patrol !=this && !npc.Patrol.Abandon) { 
                    LastPatrol = npc.Patrol;
                }

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
        public void Repet(Npc npc,double time = 100,Patrol patrol=null)
        {

            if(!(patrol ?? this).Abandon)
            {
                TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, npc), TimeSpan.FromMilliseconds(time));
            }
               
        }

        public bool PauseAuto(Npc npc)
        {
            if (Interrupt || !npc.Patrol.Running)
            {
                Pause(npc);
                return true;
            }
            return false;
        }
        public void Pause(Npc npc)
        {
            Running = false;
            PausePosition = npc.Position.Clone();
        }

        public void Stop(Npc npc)
        {
            Running = false;
            Abandon = true;

            Recovery(npc);
        }

        public void Recovery(Npc npc)
        {
            //如果当前巡航处于暂停状态则恢复当前巡航
            if (!Abandon && Running==false)
            {
                npc.Patrol.Running = true;
                Repet(npc);
                return;
            }

            //如果上次巡航不为null
            if (LastPatrol!=null && Running == false)
            {
                if (npc.Position.X == LastPatrol.PausePosition.X && npc.Position.Y == LastPatrol.PausePosition.Y && npc.Position.Z == LastPatrol.PausePosition.Z)
                {
                    LastPatrol.Running = true;
                    npc.Patrol = LastPatrol;
                    //恢复上次巡航
                    Repet(npc, 100, LastPatrol);
                }
                else
                {
                    //创建直线巡航回归上次巡航暂停点
                    Line line = new Line();
                    //不可中断，不受外力及攻击影响 类似于处于脱战状态
                    line.Interrupt = false;
                    line.Loop = false;
                    line.LastPatrol = LastPatrol;
                    //指定目标Point
                    line.Position = LastPatrol.PausePosition;
                    //恢复上次巡航
                    Repet(npc, 100, line);
                }
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
            else
            {
                //非循环任务则终止本任务并尝试恢复上次任务
                Stop(npc);
            }
        }
    }
}

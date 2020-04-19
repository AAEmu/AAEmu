using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Units
{
    /// <summary>
    /// Unit 巡逻类
    /// Unit Patrol Class
    /// </summary>
    public abstract class Patrol
    {
        /// <summary>
        /// 是否正在执行巡逻任务
        /// Are patrols under way?
        /// 默认为 False
        /// The default is False
        /// </summary>
        public bool Running { get; set; } = true;

        /// <summary>
        /// 是否为循环
        /// Is it a cycle?
        /// 默认为 True
        /// Default is True
        /// </summary>
        public bool Loop { get; set; } = true;

        /// <summary>
        /// 循环间隔 毫秒
        /// Cyclic interval milliseconds
        /// </summary>
        public double LoopDelay { get; set; }

        /// <summary>
        /// 执行进度 0-100
        /// Progress of implementation 0-100
        /// </summary>
        public sbyte Step { get; set; }

        /// <summary>
        /// 中断 True
        /// Interrupt True
        /// 如被攻击或其他行为改变自身状态等 是否终止路线
        /// Whether or not to terminate a route, such as being attacked or changing one's own state by other acts
        /// </summary>
        public bool Interrupt { get; set; } = true;

        /// <summary>
        /// 执行顺序编号
        /// Execution Sequence Number
        /// 每次执行必须递增序号，否则重复序号的动作不被执行
        /// Each execution must increment the serial number, otherwise the action of repeating the serial number will not be executed.
        /// </summary>
        public uint Seq { get; set; } = 0;

        /// <summary>
        /// 当前执行次数
        /// Current execution times
        /// </summary>
        protected uint Count { get; set; } = 0;

        /// <summary>
        /// 暂停巡航点
        /// Suspension of cruise points
        /// </summary>
        protected Point PausePosition { get; set; }

        /// <summary>
        /// 上次任务
        /// Last mission
        /// </summary>
        public Patrol LastPatrol { get; set; }

        /// <summary>
        /// 放弃任务 / Abandon mission
        /// </summary>
        public bool Abandon { get; set; } = false;
        public const float tolerance = 0.5f;


        /// <summary>
        /// 执行巡逻任务
        /// Perform patrol missions
        /// </summary>
        /// <param name="npc"></param>
        public void Apply(Npc npc)
        {
            //如果NPC不存在或不处于巡航模式或者当前执行次数不为0
            //If NPC does not exist or is not in cruise mode or the current number of executions is not zero
            if (npc.Patrol == null || (npc.Patrol.Running == false && this != npc.Patrol) || (npc.Patrol.Running == true && this == npc.Patrol))
            {
                //如果上次巡航模式处于暂停状态则保存上次巡航模式
                //If the last cruise mode is suspended, save the last cruise mode
                if (npc.Patrol != null && npc.Patrol != this && !npc.Patrol.Abandon)
                {
                    LastPatrol = npc.Patrol;
                }
                ++Count;
                //++Seq;
                Seq = (uint)Rand.Next(0, 10000);
                Running = true;
                npc.Patrol = this;
                Execute(npc);
            }
        }

        public abstract void Execute(Npc npc);

        /// <summary>
        /// 再次执行任务
        /// Perform the task again
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="time"></param>
        /// <param name="patrol"></param>
        public void Repeat(Npc npc, double time = 100, Patrol patrol = null)
        {
            if (!(patrol ?? this).Abandon)
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
            // 如果当前巡航处于暂停状态则恢复当前巡航
            // Resume current cruise if current cruise is paused
            if (!Abandon && Running == false)
            {
                npc.Patrol.Running = true;
                Repeat(npc);
                return;
            }
            // 如果上次巡航不为null
            // If the last cruise is not null
            if (LastPatrol != null && Running == false)
            {
                if (npc.Position.X == LastPatrol.PausePosition.X && npc.Position.Y == LastPatrol.PausePosition.Y && npc.Position.Z == LastPatrol.PausePosition.Z)
                {
                    LastPatrol.Running = true;
                    npc.Patrol = LastPatrol;
                    // 恢复上次巡航
                    // Resume last cruise
                    Repeat(npc, 500, LastPatrol);
                }
                else
                {
                    // 创建直线巡航回归上次巡航暂停点
                    // Create a straight cruise to return to the last cruise pause
                    var line = new Line();
                    // 不可中断，不受外力及攻击影响 类似于处于脱战状态
                    // Uninterrupted, unaffected by external forces and attacks
                    line.Interrupt = true;
                    line.Loop = false;
                    line.LastPatrol = LastPatrol;
                    // 指定目标Point
                    // Specify target point
                    line.Position = LastPatrol.PausePosition;
                    // 恢复上次巡航
                    // Resume last cruise
                    Repeat(npc, 500, line);
                }
            }
        }

        public void LoopAuto(Npc npc)
        {
            if (Loop)
            {
                Count = 0;
                //Seq = 0;
                Seq = (uint)Rand.Next(0, 10000);
                Repeat(npc, LoopDelay);
            }
            else
            {
                // 非循环任务则终止本任务并尝试恢复上次任务
                // Acyclic tasks terminate this task and attempt to resume the last task
                Stop(npc);
            }
        }
    }
}

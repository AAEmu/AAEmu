using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;

namespace AAEmu.Game.Models.Game.Units
{
    /// <summary>
    /// Unit Patrol Class
    /// </summary>
    public abstract class Patrol
    {
        /// <summary>
        /// Are patrols under way?
        /// The default is False
        /// </summary>
        public bool Running { get; set; } = true;

        /// <summary>
        /// Is it a cycle?
        /// Default is True
        /// </summary>
        public bool Loop { get; set; } = true;
        /// <summary>
        /// Cyclic interval milliseconds
        /// </summary>
        public double LoopDelay { get; set; }
        /// <summary>
        /// Progress of implementation 0-100
        /// </summary>
        public sbyte Step { get; set; }
        /// <summary>
        /// Interrupt True
        /// Whether or not to terminate a route, such as being attacked or changing one's own state by other acts
        /// </summary>
        public bool Interrupt { get; set; } = true;
        /// <summary>
        /// Execution Sequence Number
        /// Each execution must increment the serial number, otherwise the action of repeating the serial number will not be executed.
        /// </summary>
        public uint Seq { get; set; } = 0;
        /// <summary>
        /// Current execution times
        /// </summary>
        protected uint Count { get; set; } = 0;
        /// <summary>
        /// Suspension of cruise points
        /// </summary>
        protected Point PausePosition { get; set; }
        /// <summary>
        /// Last mission
        /// </summary>
        public Patrol LastPatrol { get; set; }
        /// <summary>
        /// Abandon mission
        /// </summary>
        public bool Abandon { get; set; } = false;

        protected const float Tolerance = 1.401298E-45f;	// Погрешность

        /// <summary>
        /// Perform patrol missions
        /// </summary>
        /// <param name="npc"></param>
        public void Apply(Npc npc)
        {
            // If NPC does not exist or is not in cruise mode or the current number of executions is not zero
            if (npc.Patrol == null || (npc.Patrol.Running == false && this != npc.Patrol) || (npc.Patrol.Running == true && this == npc.Patrol))
            {
                // If the last cruise mode is suspended, save the last cruise mode
                if (npc.Patrol != null && npc.Patrol != this && !npc.Patrol.Abandon)
                {
                    LastPatrol = npc.Patrol;
                }
                ++Count;
                Seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                Running = true;
                npc.Patrol = this;
                Execute(npc);
            }
        }

        public abstract void Execute(Npc npc);
        public abstract void Execute(Transfer transfer);

        /// <summary>
        /// Perform the task again
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="time"></param>
        /// <param name="patrol"></param>
        public void Repet(Npc npc, double time = 100, Patrol patrol = null)
        {
            if (!(patrol ?? this).Abandon)
            {
                TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, npc), TimeSpan.FromMilliseconds(time));
            }

        }

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
            // Resume current cruise if current cruise is paused
            if (!Abandon && Running == false)
            {
                npc.Patrol.Running = true;
                Repet(npc);
                return;
            }
            // If the last cruise is not null
            if (LastPatrol != null && Running == false)
            {
                if (Math.Abs(npc.Position.X - LastPatrol.PausePosition.X) < Tolerance && Math.Abs(npc.Position.Y - LastPatrol.PausePosition.Y) < Tolerance && Math.Abs(npc.Position.Z - LastPatrol.PausePosition.Z) < Tolerance)
                {
                    LastPatrol.Running = true;
                    npc.Patrol = LastPatrol;
                    // Resume last cruise
                    Repet(npc, 500, LastPatrol);
                }
                else
                {
                    // Create a straight cruise to return to the last cruise pause
                    var line = new Line();
                    // Uninterrupted, unaffected by external forces and attacks
                    line.Interrupt = false;
                    line.Loop = false;
                    line.LastPatrol = LastPatrol;
                    // Specify target point
                    line.Position = LastPatrol.PausePosition;
                    // Resume last cruise
                    Repet(npc, 500, line);
                }
            }
        }

        public void LoopAuto(Npc npc)
        {
            if (Loop)
            {
                Count = 0;
                Seq = 0;
                Repet(npc, LoopDelay);
            }
            else
            {
                // Acyclic tasks terminate this task and attempt to resume the last task
                Stop(npc);
            }
        }
    }
}

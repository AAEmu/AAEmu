using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;

using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    /// <summary>
    /// Unit Patrol Class
    /// </summary>
    public abstract class Patrol
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public Skill skill;
        public int idx; // index of skill
        public float ReturnDistance;
        public bool GoBack { get; set; }
        public bool InPatrol { get; set; }
        public bool InitMovement { get; set; } // инициируем точку возврвата, для возврата, если NPC ушел далеко
        public short Degree { get; set; } = 360;
        public float Time { get; set; }
        public float DeltaTime { get; set; } = 0.1f;
        public DateTime UpdateTime { get; set; }
        public ActorData moveType { get; set; }
        public double Angle { get; set; }
        public float Distance { get; set; }
        public float MovingDistance { get; set; } = 0.27f;
        public double AngleTmp { get; set; }
        public float AngVelocity { get; set; } = 5.0f;
        public float MaxVelocityForward { get; set; } = 5.4f;
        public float MaxVelocityBackward { get; set; } = 0f;
        public float VelAccel { get; set; } = 1.0f;
        public Vector3 vBeginPoint { get; set; }
        public Vector3 vEndPoint { get; set; }
        public Vector3 vMovingDistance { get; set; }
        public Vector3 vMaxVelocityForwardRun { get; set; } = new Vector3(5.4f, 5.4f, 5.4f);
        public Vector3 vMaxVelocityBackRun { get; set; } = new Vector3(-5.4f, -5.4f, -5.4f);
        public Vector3 vMaxVelocityForwardWalk { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        public Vector3 vMaxVelocityBackWalk { get; set; } = new Vector3(-1.8f, -1.8f, -1.8f);
        public Vector3 vVelocity { get; set; }
        public Vector3 vVelAccel { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        public Vector3 vPosition { get; set; }
        public Vector3 vPausePosition { get; set; }
        public Vector3 vTarget { get; set; }
        public Vector3 vDistance { get; set; }
        public float RangeToCheckPoint { get; set; } = 0.5f; // distance to checkpoint at which it is considered that we have reached it
        public Vector3 vRangeToCheckPoint { get; set; } = new Vector3(0.5f, 0.5f, 0f); // distance to checkpoint at which it is considered that we have reached it

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
        public Point PausePosition { get; set; }
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
        /// <param name="unit"></param>
        public void Apply(BaseUnit unit)
        {
            // If NPC does not exist or is not in cruise mode or the current number of executions is not zero
            if (unit.Patrol != null &&
               (unit.Patrol.Running || this == unit.Patrol) &&
               (!unit.Patrol.Running || this != unit.Patrol))
            {
                return;
            }

            // If the last cruise mode is suspended, save the last cruise mode
            if (unit.Patrol != null && unit.Patrol != this && !unit.Patrol.Abandon)
            {
                LastPatrol = unit.Patrol;
            }
            ++Count;
            Seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
            Running = true;
            unit.Patrol = this;
            //UpdateTime = DateTime.Now;
            switch (unit)
            {
                case Gimmick gimmick:
                    Execute(gimmick);
                    break;
                case Transfer transfer:
                    Execute(transfer);
                    break;
                case Npc npc:
                    Execute(npc);
                    break;
                default:
                    Execute(unit);
                    break;
            }
        }

        /// <summary>
        /// 再次执行任务
        /// Perform the task again
        /// </summary>
        /// <param name="unit"></param>
        /// <param name="time"></param>
        /// <param name="patrol"></param>
        public void Repeat(BaseUnit unit, double time = 100, Patrol patrol = null)
        {
            switch (unit)
            {
                case Npc npc:
                    if (!(patrol ?? this).Abandon)
                    {
                        TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, npc), TimeSpan.FromMilliseconds(time));
                    }

                    break;
                case Gimmick gimmick:
                    if (!(patrol ?? this).Abandon)
                    {
                        TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, gimmick), TimeSpan.FromMilliseconds(time));
                    }

                    break;
                case Transfer transfer:
                    if (!(patrol ?? this).Abandon)
                    {
                        TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, transfer), TimeSpan.FromMilliseconds(time));
                    }

                    break;
                default:
                    if (!(patrol ?? this).Abandon)
                    {
                        TaskManager.Instance.Schedule(new UnitMove(patrol ?? this, unit), TimeSpan.FromMilliseconds(time));
                    }

                    break;
            }
        }

        public bool PauseAuto(BaseUnit unit)
        {
            if (!Interrupt && unit.Patrol.Running) { return false; }

            Pause(unit);
            return true;
        }

        public void Pause(BaseUnit unit)
        {
            Running = false;
            PausePosition = unit.Position.Clone(); // сохраним точку возврата на место спавна
        }

        public void Stop(BaseUnit unit)
        {
            Running = false;
            Abandon = true;

            Recovery(unit);
        }

        public void Recovery(BaseUnit unit)
        {
            // Resume current cruise if current cruise is paused
            if (!Abandon && Running == false)
            {
                unit.Patrol.Running = true;
                Repeat(unit);
                return;
            }

            if (LastPatrol == null || Running) { return; }

            // If the last cruise is not null
            if (Math.Abs(unit.Position.X - LastPatrol.PausePosition.X) < Tolerance && Math.Abs(unit.Position.Y - LastPatrol.PausePosition.Y) < Tolerance && Math.Abs(unit.Position.Z - LastPatrol.PausePosition.Z) < Tolerance)
            {
                LastPatrol.Running = true;
                unit.Patrol = LastPatrol;
                // Resume last cruise
                Repeat(unit, 100, LastPatrol);
            }
            else
            {
                // Create a straight cruise to return to the last cruise pause
                var line = new Line();
                // Uninterrupted, unaffected by external forces and attacks
                line.Interrupt = true;
                line.Loop = false;
                line.Abandon = false;
                line.LastPatrol = LastPatrol;
                // Specify target point
                line.PausePosition = LastPatrol.PausePosition;
                // Resume last cruise
                Repeat(unit, 100, line);
            }
        }

        public void LoopAuto(BaseUnit unit)
        {
            if (Loop)
            {
                Count = 0;
                Seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                Repeat(unit, LoopDelay);
            }
            else
            {
                // Acyclic tasks terminate this task and attempt to resume the last task
                Stop(unit);
            }
        }

        protected Skill CheckSkill(Npc npc)
        {
            Skill getSkill = null;
            if (npc.Template.NpSkills.Count <= 0) { return getSkill; }

            var SkillId = npc.Template.NpSkills[idx].SkillId;
            if (npc.CooldownsSkills.ContainsKey(SkillId))
            {
                if (DateTime.Now >= npc.CooldownsSkills[SkillId])
                {
                    // удалим время cooldownEnd для скилла
                    npc.CooldownsSkills.Remove(SkillId);
                }
            }
            // можно использовать скилл
            if (npc.Template.NpSkills[idx].SkillUseConditionId == SkillUseCondition.InCombat)
            {
                getSkill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));
                if (npc.CooldownsSkills.ContainsKey(SkillId))
                {
                    if (DateTime.Now >= npc.CooldownsSkills[SkillId])
                    {
                        // добавим время cooldownEnd для скилла
                        npc.CooldownsSkills[SkillId] = DateTime.Now.AddMilliseconds(getSkill.Template.CooldownTime);
                    }
                }
                else
                {
                    // добавим время cooldownEnd для скилла
                    npc.CooldownsSkills.Add(SkillId, DateTime.Now.AddMilliseconds(getSkill.Template.CooldownTime));
                }
            }
            // USE_SEQUENCE
            idx++;
            if (idx >= npc.Template.NpSkills.Count)
            {
                idx = 0;
            }

            return getSkill;
        }

        protected void CheckBuff(Npc npc)
        {
            foreach (var npBuffs in npc.Template.NpPassiveBuffs)
            {
                var buffId = npBuffs.PassiveBuffId;
                if (npc.CooldownsBuffs.ContainsKey(buffId))
                {
                    if (DateTime.Now < npc.CooldownsBuffs[buffId])
                    {
                        // время этого баффа ещё не вышло, пробуем следующий
                        continue;
                    }
                    // удалим время cooldownEnd для баффа
                    npc.CooldownsBuffs.Remove(buffId);
                }
                // можно использовать бафф
                var buff = SkillManager.Instance.GetBuffTemplate(buffId);

                if (buff == null) { continue; }

                npc.Effects.AddEffect(new Effect(npc, npc, SkillCaster.GetByType(EffectOriginType.Passive), buff, null, DateTime.Now));
                if (npc.CooldownsBuffs.ContainsKey(buffId))
                {
                    if (DateTime.Now >= npc.CooldownsBuffs[buffId])
                    {
                        // добавим время cooldownEnd для баффа
                        npc.CooldownsBuffs[buffId] = DateTime.Now.AddSeconds(buff.Duration);
                    }
                }
                else
                {
                    // добавим время cooldownEnd для баффа
                    npc.CooldownsBuffs.Add(buffId, DateTime.Now.AddSeconds(buff.Duration));
                }
                //break;
            }
        }

        protected SkillCastTarget GetSkillCastTarget(Unit caster, Skill skill)
        {
            SkillCastTarget targetType;
            switch (skill.Template.TargetType)
            {
                case SkillTargetType.Doodad:
                    targetType = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                    targetType.ObjId = caster.CurrentTarget.ObjId;
                    break;
                case SkillTargetType.AnyUnit:
                case SkillTargetType.AnyUnitAlways:
                case SkillTargetType.Friendly:
                case SkillTargetType.FriendlyOthers:
                case SkillTargetType.GeneralUnit:
                case SkillTargetType.Hostile:
                case SkillTargetType.Others:
                case SkillTargetType.Self:
                    targetType = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                    targetType.ObjId = caster.CurrentTarget.ObjId;
                    break;
                case SkillTargetType.ArtilleryPos:
                case SkillTargetType.BallisticPos:
                case SkillTargetType.CommanderPos:
                case SkillTargetType.CursorPos:
                case SkillTargetType.Pos:
                case SkillTargetType.RelativePos:
                case SkillTargetType.SourcePos:
                case SkillTargetType.SummonPos:
                    targetType = SkillCastTarget.GetByType(SkillCastTargetType.Position);
                    targetType.ObjId = caster.CurrentTarget.ObjId;
                    break;
                case SkillTargetType.Item:
                    targetType = SkillCastTarget.GetByType(SkillCastTargetType.Item);
                    targetType.ObjId = caster.CurrentTarget.ObjId;
                    break;
                case SkillTargetType.Party:
                case SkillTargetType.Raid:
                case SkillTargetType.Line:
                case SkillTargetType.Pet:
                case SkillTargetType.Parent:
                case SkillTargetType.ChildSlave:
                case SkillTargetType.PetOwner:
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return targetType;
        }

        public abstract void Execute(BaseUnit unit);
        public abstract void Execute(Npc npc);
        public abstract void Execute(Transfer transfer);
        public abstract void Execute(Gimmick gimmick);
    }
}

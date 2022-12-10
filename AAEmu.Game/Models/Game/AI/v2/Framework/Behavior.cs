using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.AI.v2
{
    public enum BehaviorKind
    {
        // Common
        Alert,
        AlmightyAttack,
        Attack,
        Dead,
        Despawning,
        DoNothing,
        Dummy,
        FollowPath,
        FollowUnit,
        HoldPosition,
        Idle,
        ReturnState,
        Roaming,
        RunCommandSet,
        Spawning,
        Talk,

        // Archer
        ArcherAttack,

        // BigMonster
        BigMonsterAttack,

        // Flytrap
        FlytrapAlert,
        FlytrapAttack,

        // WildBoar
        WildBoarAttack
    }

    /// <summary>
    /// Represents an AI state. Called as such because of naming in the game's files.
    /// </summary>
    public abstract class Behavior
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        protected DateTime _delayEnd;
        protected float _nextTimeToDelay;

        public NpcAi Ai { get; set; }

        public abstract void Enter();
        public abstract void Tick(TimeSpan delta);
        public abstract void Exit();

        public Behavior AddTransition(TransitionEvent on, BehaviorKind kind)
        {
            return AddTransition(new Transition(on, kind));
        }

        public Behavior AddTransition(Transition transition)
        {
            return Ai.AddTransition(this, transition);
        }

        public void PickSkillAndUseIt(SkillUseConditionKind kind, BaseUnit target)
        {
            var targetDist = Ai.Owner.GetDistanceTo(target);
            // Attack behavior probably only uses base skill ?
            var skills = new List<NpcSkill>();
            if (Ai.Owner.Template.Skills.ContainsKey(kind))
            {
                skills = Ai.Owner.Template.Skills[kind];
            }
            if (skills.Count > 0)
            {
                skills = skills
                    .Where(s => !Ai.Owner.Cooldowns.CheckCooldown(s.SkillId))
                    .Where(s =>
                    {
                        var template = SkillManager.Instance.GetSkillTemplate(s.SkillId);
                        return template != null && (targetDist >= template.MinRange && targetDist <= template.MaxRange || template.TargetType == SkillTargetType.Self);
                    }).ToList();
            }

            if (targetDist == 0 && kind == SkillUseConditionKind.InIdle)
            {
                // This SkillTargetType.Self & SkillUseConditionKind.InIdle
                if (skills.Count <= 0) { return; }
                var skillSelfId = skills[Rand.Next(skills.Count)].SkillId;
                var skillTemplateSelf = SkillManager.Instance.GetSkillTemplate(skillSelfId);
                var skillSelf = new Skill(skillTemplateSelf);

                var delay1 = (int) (Ai.Owner.Template.BaseSkillDelay * 1000);
                if (Ai.Owner.Template.BaseSkillDelay == 0)
                {
                    const uint Delay1 = 10000u;
                    const uint Delay2 = 13000u;
                    delay1 = (int)Rand.Next(Delay1, Delay2);
                }

                if(this.CheckInterval(delay1))
                {
                    _log.Warn("PickSkillAndUseIt:UseSelfSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplateSelf.Id);
                    UseSkill(skillSelf, target);
                }
                return;
                //if (!Ai.Owner.Cooldowns.CheckCooldown(skillTemplateSelf.Id))
                //{
                //    _log.Warn("PickSkillAndUseIt:UseSelfSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplateSelf.Id);
                //    // TODO how to eliminate spam with skills? The following solution breaks the Npc attack
                //    if (Ai.Owner.Template.BaseSkillDelay == 0)
                //    {
                //        const uint Delay1 = 10000u;
                //        const uint Delay2 = 13000u;
                //        UseSkill(skillSelf, target, Ai.Owner.Template.BaseSkillDelay);
                //        Ai.Owner.Cooldowns.AddCooldown(skillSelf.Template.Id, (uint)Rand.Next(Delay1, Delay2));
                //        return;
                //    }
                //    UseSkill(skillSelf, target, Ai.Owner.Template.BaseSkillDelay);
                //    return;
                //}
            }

            // This SkillUseConditionKind.InCombat
            var pickedSkillId = (uint)Ai.Owner.Template.BaseSkillId;
            if (skills.Count > 0)
            {
                pickedSkillId = skills[Rand.Next(skills.Count)].SkillId;
            }
            // Hackfix for Melee attack. Needs to look at the held weapon (if any) or default to 3m. 
            if (pickedSkillId == 2 && targetDist > 4.0f) { return; }
            var skillTemplate = SkillManager.Instance.GetSkillTemplate(pickedSkillId);
            var skill = new Skill(skillTemplate);

            var delay2 = (int) (Ai.Owner.Template.BaseSkillDelay * 1000);
            if (Ai.Owner.Template.BaseSkillDelay == 0)
            {
                const uint Delay1 = 1500u;
                const uint Delay2 = 1550u;
                delay2 = (int)Rand.Next(Delay1, Delay2);
            }

            if(this.CheckInterval(delay2))
            {
                _log.Warn("PickSkillAndUseIt:UseSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2} on Target {3}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplate.Id, target.ObjId);
                UseSkill(skill, target);
            }

            //if (!Ai.Owner.Cooldowns.CheckCooldown(skillTemplate.Id))
            //{
            //    _log.Warn("PickSkillAndUseIt:UseSkill Owner.ObjId {0}, Owner.TemplateId {1}, SkillId {2} on Target {3}", Ai.Owner.ObjId, Ai.Owner.TemplateId, skillTemplate.Id, target.ObjId);
            //    // TODO how to eliminate spam with skills? The following solution breaks the Npc attack
            //    if (Ai.Owner.Template.BaseSkillDelay == 0)
            //    {
            //        const uint Delay1 = 1500u;
            //        const uint Delay2 = 1550u;
            //        UseSkill(skill, target, Ai.Owner.Template.BaseSkillDelay);
            //        Ai.Owner.Cooldowns.AddCooldown(skill.Template.Id, (uint)Rand.Next(Delay1, Delay2));
            //        return;
            //    }
            //    UseSkill(skill, target, Ai.Owner.Template.BaseSkillDelay);
            //}
        }

        // UseSkill (delay)
        public void UseSkill(Skill skill, BaseUnit target, float delay = 0)
        {
            if (target == null) { return; }
            if (skill == null) { return; }

            _nextTimeToDelay = delay;
            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = Ai.Owner.ObjId;

            SkillCastTarget skillCastTarget;
            switch (skill.Template.TargetType)
            {
                case SkillTargetType.Pos:
                    var pos = Ai.Owner.Transform.World.Position;
                    skillCastTarget = new SkillCastPositionTarget()
                    {
                        ObjId = Ai.Owner.ObjId,
                        PosX = pos.X,
                        PosY = pos.Y,
                        PosZ = pos.Z,
                        PosRot = Ai.Owner.Transform.World.ToRollPitchYawDegrees().Z // (float)MathUtil.ConvertDirectionToDegree(pos.RotationZ) //Is this rotation right?
                    };
                    break;
                default:
                    skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                    skillCastTarget.ObjId = target.ObjId;
                    break;
            }

            var skillObject = SkillObject.GetByType(SkillObjectType.None);

            skill.Callback = OnSkillEnded;
            var result = skill.Use(Ai.Owner, skillCaster, skillCastTarget, skillObject);
            if (skill.Template.TargetType == SkillTargetType.Self) { return; } // fix the eastward turn when using SelfSkill
            if (result == SkillResult.Success)
                Ai.Owner.LookTowards(target.Transform.World.Position);
        }

        public virtual void OnSkillEnded()
        {
            try
            {
                _delayEnd = DateTime.UtcNow.AddSeconds(_nextTimeToDelay);
            }
            catch
            {

            }
        }

        // Check if can pick a new skill (delay, already casting)
    }
}

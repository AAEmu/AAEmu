using System;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Params;
using AAEmu.Game.Models.Game.AI.Params.BigMonster;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class BigMonsterAttackState : State
    {
        // TODO : Use aggro list
        public Unit Target { get; set; }
        public Npc Npc { get; set; }
        public NpcTemplate OwnerTemplate { get; set; }
        public BigMonsterRoamingAiParams AiParams { get; set; }
        private DateTime _lastSkillEnd = DateTime.MinValue;
        private float _currentDelay = 0.0f;
        private float _nextDelay = 0.0f;

        public override void Enter()
        {
            base.Enter();
            if (!(AI.Owner is Npc npc))
            {
                _log.Error("State applied to invalid unit type");
                return;
            }

            Npc = npc;
            //AiParams = (BigMonsterRoamingAiParams) AiGameData.Instance.GetAiParamsForId((uint) npc.Template.NpcAiParamId);
            OwnerTemplate = npc.Template;
            _lastSkillEnd = DateTime.MinValue;
        }
        
        public override void Tick(TimeSpan delta)
        {
            if (OwnerTemplate == null)
                return;

            if (_nextDelay != 0f)
                return;

            if (Target.IsDead)
            {
                //get a new target

                GoToReturnToIdle(); //Remove once aggro finished
                return;
            }
            if (Target == null)
            {
                GoToReturnToIdle();
                return;
            }

            // Check distance to aggro list, top to bottom
            // If no one is within return distance, reset HP, MP and go back to idle position
            // TODO : Use aggro list, not single target
            var distanceToTarget = MathUtil.CalculateDistance(AI.Owner, Target, true);
            if (distanceToTarget > OwnerTemplate.ReturnDistance)
            {
                GoToReturnToIdle();
                return;
            }

            var distanceToIdle = MathUtil.CalculateDistance(AI.Owner.Transform.World.Position, AI.IdlePosition.Position, true);
            if (distanceToIdle > OwnerTemplate.AbsoluteReturnDistance)
            {
                GoToReturnToIdle();
                return;
            }

            if (Npc.SkillTask != null || Npc.ActivePlotState != null)
                return;

            if (_lastSkillEnd.AddSeconds(_currentDelay) > DateTime.UtcNow)
                return;

            var combatSkill = GetNextAiCombatSkill();

            if (combatSkill == null)
                return;//Set to base attack here?

            if (Npc.Cooldowns.CheckCooldown(combatSkill.SkillType))
                return;

            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(combatSkill.SkillType));
            // _currentDelay = nextAiSkill.Delay + (skill.Template.CastingTime / 1000.0f) + (skill.Template.ChannelingTime / 1000.0f); // TODO : Apply delay when skill **ends**
            _nextDelay = combatSkill.SkillDelay;

            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = Npc.ObjId;

            SkillCastTarget skillCastTarget;
            switch (skill.Template.TargetType)
            {
                case SkillTargetType.Pos:
                    var pos = Npc.Transform.World.Position;
                    skillCastTarget = new SkillCastPositionTarget() {
                        ObjId = Npc.ObjId,
                        PosX = pos.X,
                        PosY = pos.Y,
                        PosZ = pos.Z,
                        PosRot = Npc.Transform.World.ToRollPitchYawDegrees().Z
                        };
                    break;
                default:
                    skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
                    skillCastTarget.ObjId = Target.ObjId;
                    break;
            }

            var skillObject = SkillObject.GetByType(SkillObjectType.None);

            skill.Use(Npc, skillCaster, skillCastTarget, skillObject, true);
        }

        private void GoToReturnToIdle()
        {
            Npc.InterruptSkills();
            Npc.ClearAllAggro();
            var returnToIdleState = AI.StateMachine.GetState(Framework.States.ReturnToIdle);
            AI.StateMachine.SetCurrentState(returnToIdleState);
        }

        private BigMonsterCombatSkill GetNextAiCombatSkill()
        {
            var hpPercent = (Npc.Hp / (float)Npc.MaxHp) * 100.0f;

            var useableSkills = AiParams.CombatSkills.Where(o => !Npc.Cooldowns.CheckCooldown(o.SkillType));

            useableSkills = useableSkills.Where(o =>
            {
                return (hpPercent > o.HealthRangeMin || o.HealthRangeMin == 0) 
                && (hpPercent < o.HealthRangeMax || o.HealthRangeMax == 0);
            });

            var filteredSkills = useableSkills.ToArray();

            if (filteredSkills.Length > 0)
                return filteredSkills[Rand.Next(0, filteredSkills.Length)];
            else
                return null;
        }

        public void OnSkillEnd(Skill skill)
        {
            _lastSkillEnd = DateTime.UtcNow;
            _currentDelay = _nextDelay;
            _nextDelay = 0.0f;
        }
    }
}

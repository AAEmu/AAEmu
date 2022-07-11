using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Params;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class AlmightyAttackState : AAttackState
    {
        // TODO : Use aggro list
        public Unit Target { get; set; }
        public Npc Npc { get; set; }
        public NpcTemplate OwnerTemplate { get; set; }
        public AlmightyNpcAiParams AiParams { get; set; }
        private DateTime _lastSkillEnd = DateTime.MinValue;
        private float _currentDelay = 0.0f;
        private float _nextDelay = 0.0f;
        private int _sequenceIndex = 0;
        
        public override void Enter()
        {
            base.Enter();
            if (!(AI.Owner is Npc npc))
            {
                _log.Error("State applied to invalid unit type");
                return;
            }

            Npc = npc;
            //AiParams = (AlmightyNpcAiParams) AiGameData.Instance.GetAiParamsForId((uint) npc.Template.NpcAiParamId);
            OwnerTemplate = npc.Template;
            _lastSkillEnd = DateTime.MinValue;
        }

        public override void Tick(TimeSpan delta)
        {
            return;
            /*
            if (OwnerTemplate == null)
                return;

            if (HasAnyAggro() == false)
            {
                GoToReturnToIdle();
                return;
            }

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
            var distanceToTarget = MathUtil.CalculateDistance(AI.Owner.Transform.World.Position, Target.Transform.World.Position, true);
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
            
            if (_lastSkillEnd + TimeSpan.FromSeconds(_currentDelay) > DateTime.UtcNow)
                return; 

            var aiSkillList = GetNextAiSkills();
            if (aiSkillList == null)
                return;

            if (aiSkillList.Skills.Count <= 0)
                return;

            var skillIndex = Math.Min(aiSkillList.Skills.Count - 1, Rand.Next(0, aiSkillList.Skills.Count)); // hacky, not sure if rand is inclusive
            var nextAiSkill = aiSkillList.Skills[skillIndex]; // TODO: Do we pick at random ? 

            if (Npc.Cooldowns.CheckCooldown(nextAiSkill.SkillId))
                return;
            
            var skill = new Skill(SkillManager.Instance.GetSkillTemplate(nextAiSkill.SkillId));
            // _currentDelay = nextAiSkill.Delay + (skill.Template.CastingTime / 1000.0f) + (skill.Template.ChannelingTime / 1000.0f); // TODO : Apply delay when skill **ends**
            _nextDelay = nextAiSkill.Delay;
            
            var skillCaster = SkillCaster.GetByType(SkillCasterType.Unit);
            skillCaster.ObjId = Npc.ObjId;

            var skillCastTarget = SkillCastTarget.GetByType(SkillCastTargetType.Unit);
            skillCastTarget.ObjId = Target.ObjId;

            var skillObject = SkillObject.GetByType(SkillObjectType.None);

            skill.Use(Npc, skillCaster, skillCastTarget, skillObject, true);
            */
        }

        private AiSkillList GetNextAiSkills()
        {
            if (_sequenceIndex >= AiParams.AiSkillLists.Count)
                _sequenceIndex = 0;

            var aiSkill = AiParams.AiSkillLists[_sequenceIndex];

            _sequenceIndex++;

            var hpPercent = (Npc.Hp / (float)Npc.MaxHp) * 100.0f;

            if ((hpPercent < aiSkill.HealthRangeMin && aiSkill.HealthRangeMin != 0) 
                || (hpPercent > aiSkill.HealthRangeMax && aiSkill.HealthRangeMax != 0))
                return null;

            if (aiSkill.Dice > 0 && Rand.Next(0, 1000) > aiSkill.Dice)
                return null;
            
            return aiSkill;
        }

        private void GoToReturnToIdle()
        {
            Npc.InterruptSkills();
            Npc.ClearAllAggro();
            var returnToIdleState = AI.StateMachine.GetState(Framework.States.ReturnToIdle);
            AI.StateMachine.SetCurrentState(returnToIdleState);
        }

        public void OnSkillEnd(Skill skill)
        {
            _lastSkillEnd = DateTime.UtcNow;
            _currentDelay = _nextDelay;
            _nextDelay = 0.0f;
        }
    }
}

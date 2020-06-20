using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotEventTemplate
    {
        public uint Id { get; set; }
        public uint PlotId { get; set; }
        public int Position { get; set; }
        public uint SourceUpdateMethodId { get; set; }
        public uint TargetUpdateMethodId { get; set; }
        public int TargetUpdateMethodParam1 { get; set; }
        public int TargetUpdateMethodParam2 { get; set; }
        public int TargetUpdateMethodParam3 { get; set; }
        public int TargetUpdateMethodParam4 { get; set; }
        public int TargetUpdateMethodParam5 { get; set; }
        public int TargetUpdateMethodParam6 { get; set; }
        public int TargetUpdateMethodParam7 { get; set; }
        public int TargetUpdateMethodParam8 { get; set; }
        public int TargetUpdateMethodParam9 { get; set; }
        public int Tickets { get; set; }
        public bool AoeDiminishing { get; set; }
        public LinkedList<PlotEventCondition> Conditions { get; set; }
        public LinkedList<PlotEventEffect> Effects { get; set; }
        public LinkedList<PlotNextEvent> NextEvents { get; set; }

        public PlotEventTemplate()
        {
            Conditions = new LinkedList<PlotEventCondition>();
            Effects = new LinkedList<PlotEventEffect>();
            NextEvents = new LinkedList<PlotNextEvent>();
        }

        public bool GetConditionResult(PlotInstance instance,PlotCondition condition)
        {
            var not = condition.NotCondition;
            //Check if condition was cached
            if (instance.UseConditionCache(condition))
            {
                var cacheResult = instance.GetConditionCacheResult(condition);

                //Apply not condition
                cacheResult = not ? !cacheResult : cacheResult;

                return cacheResult;
            }

            //Check 
            if (condition.Check(instance.Caster, instance.CasterCaster, instance.Target, instance.TargetCaster, instance.SkillObject))
            {
                //We need to undo the not condition to store in cache
                instance.UpdateConditionCache(condition, not ? false : true);
                return true;
            }
            else
            {
                instance.UpdateConditionCache(condition, not ? true : false);
                return false;
            }
        }

        public bool СheckСonditions(PlotInstance instance)
        {
            foreach (var condition in Conditions)
            {
                if (!GetConditionResult(instance, condition.Condition))
                    return false;
            }

            return true;
        }

        public async Task ApplyDelay(PlotNextEvent nextEvent)
        {
            //TODO Apply Attack Speed / Speed / Cast Time / Etc
            NLog.LogManager.GetCurrentClassLogger().
                            Error($"PlotEvent: {Id} NextEvent: {nextEvent.Event.Id} Delay: {nextEvent.Delay}");
            await Task.Delay(nextEvent.Delay);

            if (nextEvent.AddAnimCsTime)
            {
                foreach(var effect in Effects)
                {
                    var template = SkillManager.Instance.GetEffectTemplate(effect.ActualId, effect.ActualType);
                    if (template is SpecialEffect specialEffect)
                    {
                        if (specialEffect.SpecialEffectTypeId == SpecialType.Anim)
                        {
                            NLog.LogManager.GetCurrentClassLogger().
                            Error($"PlotEvent: {Id} NextEvent: {nextEvent.Event.Id} AnimDelay: {specialEffect.Value1}");
                            await Task.Delay(specialEffect.Value1);
                        }
                    }
                }
            }
        }

        public void ApplyEffects(PlotInstance instance)
        {
            var skill = instance.ActiveSkill;
            foreach (var eff in Effects)
            {
                var template = SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType);
                if (template is BuffEffect)
                {
                    instance.Flag = 6; //idk what this does?
                }

                if (template is DamageEffect)
                    NLog.LogManager.GetCurrentClassLogger().
                    Error($"PlotEvent: {Id} ----------APPLIED DMG-------------");

                template.Apply(
                    instance.Caster,
                    instance.CasterCaster,
                    instance.Target,
                    instance.TargetCaster,
                    new CastPlot(PlotId, skill.TlId, Id, skill.Template.Id), skill, instance.SkillObject, DateTime.Now);
            }
        }

        public async Task PlayEvent(PlotInstance instance)
        {
            instance.Flag = 2;

            var CNext = instance.CurrentNextEvent;

            // Check Conditions
            bool pass = СheckСonditions(instance);
            if (pass)
                ApplyEffects(instance);
            else
                instance.Flag = 0;

            //This is pasted from old code. not sure what to do here
            var skill = instance.ActiveSkill;
            var unkId = ((CNext?.Casting ?? false) || (CNext?.Channeling ?? false)) ? instance.Caster.ObjId : 0;
            var casterPlotObj = new PlotObject(instance.Caster);
            var targetPlotObj = new PlotObject(instance.Target);
            instance.Caster.BroadcastPacket(new SCPlotEventPacket(skill.TlId, Id, skill.Template.Id, casterPlotObj, targetPlotObj, unkId, 0, instance.Flag), true);

            //Do tickets
            if (instance.Tickets.ContainsKey(Id))
                instance.Tickets[Id]++;
            else
                instance.Tickets.Add(Id, 1);

            //Check if we hit max tickets
            if (instance.Tickets[Id] > Tickets && Tickets > 1)
            {
                //Max Recursion. Leave Scope
                return;
            }

            // I think we apply delays here..
            // todo
            NLog.LogManager.GetCurrentClassLogger().
                Error($"PlotEvent: {Id} Pass: {pass}");
            List<PlotNextEvent> backwards = new List<PlotNextEvent>();
            foreach (var nextEvent in NextEvents)
            {
                if ((pass && !nextEvent.Fail) || (!pass && nextEvent.Fail))
                {
                    instance.CurrentNextEvent = nextEvent;
                    await ApplyDelay(nextEvent);
                    await nextEvent.Event.PlayEvent(instance);
                }
            }
        }

        public virtual bool СheckСonditions(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var result = true;
            foreach (var condition in Conditions)
            {
                if (condition.Condition.Check(caster, casterCaster, target, targetCaster, skillObject))
                    continue;
                result = false;
                break;
            }

            return result;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Static;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotNode
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        // Tree
        public PlotTree Tree;
        public List<PlotNode> Children;
        // Plots
        public PlotEventTemplate Event;
        public PlotNextEvent ParentNextEvent;
        

        public PlotNode()
        {
            Children = new List<PlotNode>();
        }

        public int ComputeDelayMs(PlotState state, PlotTargetInfo targetInfo)
        {
            return ParentNextEvent.GetDelay(state, targetInfo, this);
        }
        
        public bool CheckConditions(PlotState state, PlotTargetInfo targetInfo)
        {
            return Event.Conditions.All(condition => condition.CheckCondition(state, targetInfo));
        }

        public void Execute(PlotState state, PlotTargetInfo targetInfo)
        {
            //_log.Debug("Executing plot node with id {0}", Event.Id);

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            byte flag = 2;
            foreach (var eff in Event.Effects)
            {
                eff.ApplyEffect(state, targetInfo, Event, ref flag);
            }
            
            double castTime = Event.NextEvents
                 .Where(nextEvent => (nextEvent.Casting || nextEvent.Channeling))
                 .Aggregate(0, (current, nextEvent) => (current > nextEvent.Delay) ? current : (nextEvent.Delay / 10));
            castTime = state.Caster.ApplySkillModifiers(state.ActiveSkill, SkillAttribute.CastTime, castTime);
            castTime = Math.Max(castTime, 0);

            if (Event.Effects
                .Select(eff => SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType))
                .OfType<SpecialEffect>()
                .Any())
            {
                var skill = state.ActiveSkill;
                var unkId = ((ParentNextEvent?.Casting ?? false) || (ParentNextEvent?.Channeling ?? false)) ? state.Caster.ObjId : 0;

                PlotObject casterPlotObj;
                if (targetInfo.Source.ObjId == uint.MaxValue)
                    casterPlotObj = new PlotObject(targetInfo.Source.Position);
                else
                    casterPlotObj = new PlotObject(targetInfo.Source);

                PlotObject targetPlotObj;
                if (targetInfo.Target.ObjId == uint.MaxValue)
                    targetPlotObj = new PlotObject(targetInfo.Target.Position);
                else
                    targetPlotObj = new PlotObject(targetInfo.Target);

                byte targetCount = (byte)targetInfo.EffectedTargets.Count();
                state.Caster.BroadcastPacket(new SCPlotEventPacket(skill.TlId, Event.Id, skill.Template.Id, casterPlotObj, targetPlotObj, unkId, (ushort)castTime, flag, 0, targetCount), true);
                _log.Debug($"Execute Took {stopwatch.ElapsedMilliseconds} to finish.");
            }
        }
    }
}

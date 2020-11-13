﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
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
        public PlotNode Parent;
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
            return ParentNextEvent.GetDelay(state, targetInfo, Parent);
        }
        
        public bool CheckConditions(PlotState state, PlotTargetInfo targetInfo)
        {
            return Event.Conditions.All(condition => condition.CheckCondition(state, targetInfo));
        }

        public void Execute(PlotState state, PlotTargetInfo targetInfo, CompressedGamePackets packets = null)
        {
            //_log.Debug("Executing plot node with id {0}", Event.Id);

            var stopwatch = new Stopwatch();
            stopwatch.Start();
            byte flag = 2;
            foreach (var eff in Event.Effects)
            {
                try
                {
                    eff.ApplyEffect(state, targetInfo, Event, ref flag);
                }
                catch (Exception e)
                {
                    state?.Caster?.SendPacket(new SCChatMessagePacket(Chat.ChatType.Notice, "Plot Effects Error - Check Logs"));
                    _log.Error("[Plot Effects Error]: {0}\n{1}", e.Message, e.StackTrace);
                }
            }

            double castTime = Event.NextEvents
                 .Where(nextEvent => (nextEvent.Casting || nextEvent.Channeling))
                 .Max(nextEvent => nextEvent.Delay / 10 as int?) ?? 0;
            castTime = state.Caster.ApplySkillModifiers(state.ActiveSkill, SkillAttribute.CastTime, castTime);
            castTime = Math.Max(castTime, 0);

            if (castTime > 0)
                state.IsCasting = true;
            if ((ParentNextEvent?.Casting ?? false) || (ParentNextEvent?.Casting ?? false))
                state.IsCasting = false;

            if (Event.HasSpecialEffects() || castTime > 0)
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

                var packet = new SCPlotEventPacket(skill.TlId, Event.Id, skill.Template.Id, casterPlotObj,
                    targetPlotObj, unkId, (ushort)castTime, flag, 0, targetCount);
                
                if (packets != null)
                    packets.AddPacket(packet);
                else
                    state.Caster.BroadcastPacket(packet, true);
                
                _log.Debug($"Execute Took {stopwatch.ElapsedMilliseconds} to finish.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotEventTemplate
    {
        public uint Id { get; set; }
        public NewPlot Plot { get; set; }
        
        public int Tickets { get; set; }
        public bool AoeDiminishing { get; set; }
        public int Position { get; set; }
        
        public PlotSourceUpdateMethodType SourceUpdateMethod { get; set; }
        
        public PlotTargetUpdateMethodType TargetUpdateMethodType { get; set; }
        public int TargetUpdateMethodParam1 { get; set; }
        public int TargetUpdateMethodParam2 { get; set; }
        public int TargetUpdateMethodParam3 { get; set; }
        public int TargetUpdateMethodParam4 { get; set; }
        public int TargetUpdateMethodParam5 { get; set; }
        public int TargetUpdateMethodParam6 { get; set; }
        public int TargetUpdateMethodParam7 { get; set; }
        public int TargetUpdateMethodParam8 { get; set; }
        public int TargetUpdateMethodParam9 { get; set; }
        
        public SortedList<uint, NewPlotNextEventTemplate> NextEvents { get; set; }
        public SortedList<uint, NewPlotEventCondition> Conditions { get; set; }
        public SortedList<uint, NewPlotEffect> Effects { get; set; }

        public NewPlotEventTemplate()
        {
            Conditions = new SortedList<uint, NewPlotEventCondition>();
            NextEvents = new SortedList<uint, NewPlotNextEventTemplate>();
            Effects = new SortedList<uint, NewPlotEffect>();
        }

        public void Execute(PlotCaster caster, SkillCaster skillCaster, PlotTarget target, SkillCastTarget skillCastTarget, SkillObject skillObject, ushort tlId, Skill skill, Dictionary<uint, int> callCounter)
        {
            var flag = 2;
            for (int i = 0; i < Tickets; i++)
            {
                if (callCounter.ContainsKey(Id))
                {
                    callCounter[Id] += 1;
                    if (callCounter[Id] > Tickets)
                        return;
                }
                else
                {
                    callCounter.Add(Id, 1);
                }
                
                var updatedSource = UpdateSource(caster, target);
                var updatedTarget = UpdateTarget(caster, target);
                // Console.WriteLine("Plot {0}, step {1}, ticket {2}, updated source {3}, updated target {4}", this.Plot.Id, this.Id, i, updatedSource.Name, updatedTarget.Name);

                foreach (var condition in Conditions.Values)
                {
                    
                    if (condition.Execute(updatedSource, skillCaster, updatedTarget, skillCastTarget, skillObject))
                    {
                        Console.WriteLine("Plot condition ev {0} type {1} result {2}", Id, condition.Condition.Kind, true);
                        continue;
                    }
                    
                    Console.WriteLine("Plot condition ev {0} type {1} result {2}", Id, condition.Condition.Kind, false);
                    
                    flag = 0;
                    break;
                }

                if (flag == 0)
                    continue; // This will go to next ticket

                foreach (var effect in Effects.Values)
                {
                    effect.Apply(updatedSource, skillCaster, updatedTarget, skillCastTarget, skillObject, tlId, skill);
                }
                
                caster.PreviousCaster = updatedSource;
                target.PreviousTarget = updatedTarget;
            }

            // Send event packet
            caster.OriginalCaster.BroadcastPacket(new SCPlotEventPacket(tlId, Id, skill.Template.Id, new PlotObject(caster.OriginalCaster), new PlotObject(target.OriginalTarget), 0, 0, 0), true);

                
            foreach (var (pos, nextEvent) in NextEvents)
            {
                nextEvent.Execute(caster, skillCaster, target, skillCastTarget, skillObject, tlId, skill, callCounter);
            }
        }

        public Unit UpdateSource(PlotCaster caster, PlotTarget target)
        {
            switch (SourceUpdateMethod)
            {
                case PlotSourceUpdateMethodType.OriginalSource:
                    return caster.OriginalCaster;
                case PlotSourceUpdateMethodType.OriginalTarget:
                    return target.OriginalTarget;
                case PlotSourceUpdateMethodType.PreviousSource:
                    return caster.PreviousCaster;
                case PlotSourceUpdateMethodType.PreviousTarget:
                    return target.PreviousTarget;
                default:
                    return null;
            }
        }

        public Unit UpdateTarget(PlotCaster caster, PlotTarget target)
        {
            switch (TargetUpdateMethodType)
            {
                case PlotTargetUpdateMethodType.OriginalSource:
                    return caster.OriginalCaster;
                case PlotTargetUpdateMethodType.OriginalTarget:
                    return target.OriginalTarget;
                case PlotTargetUpdateMethodType.PreviousSource:
                    return caster.PreviousCaster;
                case PlotTargetUpdateMethodType.PreviousTarget:
                    return target.PreviousTarget;
                case PlotTargetUpdateMethodType.RandomArea:
                    // TODO implement
                case PlotTargetUpdateMethodType.Area:
                    // TODO implement
                case PlotTargetUpdateMethodType.RandomUnit:
                    // TODO implement
                default: return null;
            }
        }
    }
}

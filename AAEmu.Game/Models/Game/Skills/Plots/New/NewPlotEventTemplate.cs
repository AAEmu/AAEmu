using System.Collections.Generic;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.New
{
    public class NewPlotEventTemplate
    {
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
        
        public List<NewPlotNextEventTemplate> NextEvents { get; set; }
        public SortedList<uint, NewPlotEventCondition> Conditions { get; set; }
        public SortedList<uint, NewPlotEffect> Effects { get; set; }

        public void Execute(PlotCaster caster, PlotTarget target)
        {
            var flag = 2;
            for (int i = 0; i < Tickets; i++)
            {
                var updatedSource = UpdateSource(caster, target);
                var updatedTarget = UpdateTarget(caster, target);

                foreach (var condition in Conditions.Values)
                {
                    if (condition.Execute())
                        continue;
                    
                    flag = 0;
                    break;
                }

                if (flag == 0)
                    continue; // This will go to next ticket

                foreach (var effect in Effects.Values)
                {
                    effect.Apply();
                }

                // Send event packet

                caster.PreviousCaster = updatedSource;
                target.PreviousTarget = updatedTarget;
                
                foreach (var nextEvent in NextEvents)
                {
                    nextEvent.Execute(caster, target);
                }
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

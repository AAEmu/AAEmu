using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotTargetInfo
    {
        private Logger _log = NLog.LogManager.GetCurrentClassLogger();

        public BaseUnit Source { get; set; }
        private BaseUnit PreviousSource { get; set; }
        public BaseUnit Target { get; set; }
        private BaseUnit PreviousTarget { get; set; }
        public List<BaseUnit> EffectedTargets { get; set; }

        public PlotTargetInfo(PlotState state)
        {
            EffectedTargets = new List<BaseUnit>();
            PreviousSource = state.Caster;
            PreviousTarget = state.Target;
        }

        public PlotTargetInfo(PlotTargetInfo targetInfo)
        {
            EffectedTargets = new List<BaseUnit>();
            PreviousSource = targetInfo.Source;
            PreviousTarget = targetInfo.Target;
            Source = targetInfo.Source;
            Target = targetInfo.Target;
        }

        public void UpdateTargetInfo(PlotEventTemplate template, PlotState state)
        {
            UpdateSource(template, state);
            UpdateTargets(template, state);
        }
        
        public void UpdateSource(PlotEventTemplate template, PlotState state)
        {
            switch ((PlotSourceUpdateMethodType)template.SourceUpdateMethodId)
            {
                case PlotSourceUpdateMethodType.OriginalSource:
                    Source = state.Caster;
                    break;
                case PlotSourceUpdateMethodType.OriginalTarget:
                    Source = state.Target;
                    break;
                case PlotSourceUpdateMethodType.PreviousSource:
                    Source = PreviousSource;
                    break;
                case PlotSourceUpdateMethodType.PreviousTarget:
                    Source = PreviousTarget;
                    break;
            }
        }

        public void UpdateTargets(PlotEventTemplate template, PlotState state)
        {
            switch ((PlotTargetUpdateMethodType)template.TargetUpdateMethodId)
            {
                case PlotTargetUpdateMethodType.OriginalSource:
                    Target = state.Caster;
                    EffectedTargets.Add(Target);
                    break;
                case PlotTargetUpdateMethodType.OriginalTarget:
                    Target = state.Target;
                    EffectedTargets.Add(Target);
                    break;
                case PlotTargetUpdateMethodType.PreviousSource:
                    Target = PreviousSource;
                    EffectedTargets.Add(Target);
                    break;
                case PlotTargetUpdateMethodType.PreviousTarget:
                    Target = PreviousTarget;
                    EffectedTargets.Add(Target);
                    break;
                case PlotTargetUpdateMethodType.Area:
                    Target = UpdateAreaTarget(new PlotTargetAreaParams(template), state);
                    break;
                case PlotTargetUpdateMethodType.RandomUnit:
                    Target = UpdateRandomUnitTarget(new PlotTargetRandomUnitParams(template), state);
                    EffectedTargets.Add(Target);
                    break;
                case PlotTargetUpdateMethodType.RandomArea:
                    Target = UpdateRandomAreaTarget(new PlotTargetRandomAreaParams(template), state);
                    break;
            }
        }

        private BaseUnit UpdateAreaTarget(PlotTargetAreaParams args, PlotState state)
        {
            BaseUnit posUnit = new BaseUnit();
            posUnit.ObjId = uint.MaxValue;
            posUnit.Region = PreviousTarget.Region;
            posUnit.Position = new Point();
            posUnit.Position.ZoneId = PreviousTarget.Position.ZoneId;
            posUnit.Position.WorldId = PreviousTarget.Position.WorldId;

            //TODO Optimize rotation calc 
            var rotZ = PreviousTarget.Position.RotationZ;
            if (args.Angle != 0)
                rotZ = MathUtil.ConvertDegreeToDirection(args.Angle + MathUtil.ConvertDirectionToDegree(PreviousTarget.Position.RotationZ));

            float x, y;
            if (args.Distance > 0)
                (x, y) = MathUtil.AddDistanceToFront(args.Distance / 1000, PreviousTarget.Position.X, PreviousTarget.Position.Y, rotZ);
            else
                (x, y) = (PreviousTarget.Position.X, PreviousTarget.Position.Y);

            posUnit.Position.X = x;
            posUnit.Position.Y = y;
            posUnit.Position.Z = PreviousTarget.Position.Z;
            posUnit.Position.RotationZ = rotZ;
            // TODO use heightmap for Z coord 
            
            if (args.MaxTargets == 0)
            {
                EffectedTargets.Add(posUnit);
                return posUnit;
            }
            
            // posUnit.Position.Z = get heightmap value for x:y     
            //TODO: Get Targets around posUnit?
            var unitsInRange = FilterTargets(WorldManager.Instance.GetAround<Unit>(posUnit, 5), state, args);

            // TODO : Filter min distance
            // TODO : Compute Unit Relation
            // TODO : Compute Unit Flag
            // unitsInRange = unitsInRange.Where(u => u.);

            EffectedTargets.AddRange(unitsInRange);
            state.HitObjects.AddRange(unitsInRange);

            return posUnit;
        }

        private BaseUnit UpdateRandomUnitTarget(PlotTargetRandomUnitParams args, PlotState state)
        {
            //TODO for now we get all units in a 5 meters radius
            var randomUnits = WorldManager.Instance.GetAround<Unit>(Source, 5);

            var filteredUnits = FilterTargets(randomUnits, state, args);
            var index = Rand.Next(0, randomUnits.Count);
            var randomUnit = filteredUnits.ElementAt(index);

            return randomUnit;
        }

        private BaseUnit UpdateRandomAreaTarget(PlotTargetRandomAreaParams args, PlotState state)
        {
            BaseUnit posUnit = new BaseUnit();
            posUnit.ObjId = uint.MaxValue;
            posUnit.Region = PreviousTarget.Region;
            posUnit.Position = new Point();
            posUnit.Position.ZoneId = PreviousTarget.Position.ZoneId;
            posUnit.Position.WorldId = PreviousTarget.Position.WorldId;

            //TODO Optimize rotation calc 
            var rotZ = PreviousTarget.Position.RotationZ;
            int angle = Rand.Next(-180, 180);
            if (angle != 0)
                rotZ = MathUtil.ConvertDegreeToDirection(angle + MathUtil.ConvertDirectionToDegree(PreviousTarget.Position.RotationZ));

            float x, y;
            float distance = Rand.Next(0, (float)args.Distance);
            if (distance > 0)
                (x, y) = MathUtil.AddDistanceToFront(distance / 1000, PreviousTarget.Position.X, PreviousTarget.Position.Y, rotZ);
            else
                (x, y) = (PreviousTarget.Position.X, PreviousTarget.Position.Y);

            posUnit.Position.X = x;
            posUnit.Position.Y = y;
            posUnit.Position.Z = PreviousTarget.Position.Z;
            posUnit.Position.RotationZ = rotZ;
            // TODO use heightmap for Z coord 

            if (args.MaxTargets == 0)
            {
                EffectedTargets.Add(posUnit);
                return posUnit;
            }

            // posUnit.Position.Z = get heightmap value for x:y     
            //TODO: Get Targets around posUnit?
            var unitsInRange = FilterTargets(WorldManager.Instance.GetAround<Unit>(posUnit, 5), state, args);

            // TODO : Filter min distance
            // TODO : Compute Unit Relation
            // TODO : Compute Unit Flag
            // unitsInRange = unitsInRange.Where(u => u.);

            EffectedTargets.AddRange(unitsInRange);
            state.HitObjects.AddRange(unitsInRange);

            return posUnit;
        }

        private IEnumerable<Unit> FilterTargets(IEnumerable<Unit> units, PlotState state, IPlotTargetParams args)
        {
            var template = state.ActiveSkill.Template;
            var filtered = units;
            if (!template.TargetAlive)
                filtered = filtered.Where(o => o.Hp == 0);
            if (!template.TargetDead)
                filtered = filtered.Where(o => o.Hp > 0);
            if (args.HitOnce)
                filtered = filtered.Where(o => !state.HitObjects.Contains(o));
            
            filtered = filtered
                .Where(o =>
                {
                    var relationState = state.Caster.Faction.GetRelationState(o.Faction.Id);
                    if (relationState == RelationState.Neutral) // TODO ?
                        return false;
                    
                    switch (args.UnitRelationType)
                    {
                        case SkillTargetRelation.Any:
                            return true;
                        case SkillTargetRelation.Friendly:
                            return relationState == RelationState.Friendly;
                        case SkillTargetRelation.Party:
                            return false; // TODO filter party member
                        case SkillTargetRelation.Raid:
                            return false; // TODO filter raid member
                        case SkillTargetRelation.Hostile:
                            return relationState == RelationState.Hostile;
                        default:
                            return true;
                    }
                });

            filtered = filtered.Where(o => ((byte)o.TypeFlag & args.UnitTypeFlag) != 0);


            return filtered;
        }
    }
}

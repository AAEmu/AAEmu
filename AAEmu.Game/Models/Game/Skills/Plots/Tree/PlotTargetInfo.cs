using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;
using AAEmu.Game.Models.Game.Skills.Utils;
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

        public PlotTargetInfo(BaseUnit source, BaseUnit target)
        {
            EffectedTargets = new List<BaseUnit>();
            PreviousSource = source;
            PreviousTarget = target;
            Source = source;
            Target = target;
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
                    Target = UpdateAreaTarget(new PlotTargetAreaParams(template), state, template);
                    break;
                case PlotTargetUpdateMethodType.RandomUnit:
                    Target = UpdateRandomUnitTarget(new PlotTargetRandomUnitParams(template), state, template);
                    break;
                case PlotTargetUpdateMethodType.RandomArea:
                    Target = UpdateRandomAreaTarget(new PlotTargetRandomAreaParams(template), state, template);
                    break;
            }
        }

        private BaseUnit UpdateAreaTarget(PlotTargetAreaParams args, PlotState state, PlotEventTemplate plotEvent)
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
                rotZ = MathUtil.ConvertDegreeToDirection(-args.Angle + MathUtil.ConvertDirectionToDegree(PreviousTarget.Position.RotationZ));

            float x, y;
            if (args.Distance != 0)
                (x, y) = MathUtil.AddDistanceToFront((args.Distance / 1000.0f) - 0.01f, PreviousTarget.Position.X, PreviousTarget.Position.Y, rotZ);
            // We substract 0.1 here to help with floating point errors
            else
                (x, y) = (PreviousTarget.Position.X, PreviousTarget.Position.Y);

            posUnit.Position.X = x;
            posUnit.Position.Y = y;
            posUnit.Position.Z = PreviousTarget.Position.Z + (args.HeightOffset / 1000f);
            posUnit.Position.RotationZ = rotZ;
            // TODO use heightmap for Z coord 
            
            if (args.MaxTargets == 0)
            {
                EffectedTargets.Add(posUnit);
                return posUnit;
            }
            
            // posUnit.Position.Z = get heightmap value for x:y     
            //TODO: Get Targets around posUnit?
            var unitsInRange = FilterTargets(WorldManager.Instance.GetAroundByShape<Unit>(posUnit, args.Shape), state, args, plotEvent);
            unitsInRange = unitsInRange.Take(args.MaxTargets);
            // TODO : Filter min distance
            // TODO : Compute Unit Relation
            // TODO : Compute Unit Flag
            // unitsInRange = unitsInRange.Where(u => u.);

            EffectedTargets.AddRange(unitsInRange);
            if (state.HitObjects.ContainsKey(plotEvent.Id))
            {
                state.HitObjects[plotEvent.Id].AddRange(unitsInRange);
            }
            else
            {
                state.HitObjects.Add(plotEvent.Id, new List<GameObject>(unitsInRange));
            }

            return posUnit;
        }

        private BaseUnit UpdateRandomUnitTarget(PlotTargetRandomUnitParams args, PlotState state, PlotEventTemplate plotEvent)
        {
            //TODO for now we get all units in a 5 meters radius
            var randomUnits = WorldManager.Instance.GetAroundByShape<Unit>(Source, args.Shape);

            var filteredUnits = FilterTargets(randomUnits, state, args, plotEvent);
            if (args.HitOnce)
                filteredUnits = filteredUnits.Where(unit => unit.ObjId != PreviousTarget.ObjId);

            var index = Rand.Next(0, filteredUnits.Count());

            if (filteredUnits.Count() == 0)
                return null;

            var randomUnit = filteredUnits.ElementAt(index);

            EffectedTargets.Add(randomUnit);
            if (state.HitObjects.ContainsKey(plotEvent.Id))
            {
                state.HitObjects[plotEvent.Id].Add(randomUnit);
            }
            else
            {
                state.HitObjects.Add(plotEvent.Id, new List<GameObject>{ randomUnit });
            }

            return randomUnit;
        }

        private BaseUnit UpdateRandomAreaTarget(PlotTargetRandomAreaParams args, PlotState state, PlotEventTemplate plotEvent)
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
            if (distance != 0)
                (x, y) = MathUtil.AddDistanceToFront(distance / 1000, PreviousTarget.Position.X, PreviousTarget.Position.Y, rotZ);
            else
                (x, y) = (PreviousTarget.Position.X, PreviousTarget.Position.Y);

            posUnit.Position.X = x;
            posUnit.Position.Y = y;
            posUnit.Position.Z = PreviousTarget.Position.Z + (args.HeightOffset / 1000f);
            posUnit.Position.RotationZ = rotZ;
            // TODO use heightmap for Z coord 

            if (args.MaxTargets == 0)
            {
                EffectedTargets.Add(posUnit);
                return posUnit;
            }

            // posUnit.Position.Z = get heightmap value for x:y     
            //TODO: Get Targets around posUnit?
            var unitsInRange = FilterTargets(WorldManager.Instance.GetAroundByShape<Unit>(posUnit, args.Shape), state, args, plotEvent);
            unitsInRange = unitsInRange.Take(args.MaxTargets);

            // TODO : Filter min distance
            // TODO : Compute Unit Relation
            // TODO : Compute Unit Flag
            // unitsInRange = unitsInRange.Where(u => u.);

            EffectedTargets.AddRange(unitsInRange);
            if (state.HitObjects.ContainsKey(plotEvent.Id))
            {
                state.HitObjects[plotEvent.Id].AddRange(unitsInRange);
            }
            else
            {
                state.HitObjects.Add(plotEvent.Id, new List<GameObject>(unitsInRange));
            }

            return posUnit;
        }

        private IEnumerable<Unit> FilterTargets(IEnumerable<Unit> units, PlotState state, IPlotTargetParams args, PlotEventTemplate plotEvent)
        {
            var template = state.ActiveSkill.Template;
            var filtered = units;
            if (!template.TargetAlive)
                filtered = filtered.Where(o => o.Hp == 0);
            if (!template.TargetDead)
                filtered = filtered.Where(o => o.Hp > 0);
            if (args.HitOnce)
            {
                filtered = filtered.Where(o =>
                {
                    if (state.HitObjects.ContainsKey(plotEvent.Id))
                        return !state.HitObjects[plotEvent.Id].Contains(o);
                    else
                        return true;
                });
            }
            
            filtered = filtered 
                .Where(o =>
                {
                    var relationState = state.Caster.GetRelationStateTo(o);
                    if (relationState == RelationState.Neutral) // TODO ?
                        return false;
                    return true;
                });
            
            filtered = SkillTargetingUtil.FilterWithRelation(args.UnitRelationType, state.Caster, filtered);
            filtered = filtered.Where(o => ((byte)o.TypeFlag & args.UnitTypeFlag) != 0);

            return filtered;
        }
    }
}

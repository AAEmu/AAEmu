using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
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
        public LinkedList<PlotAoeCondition> AoeConditions { get; set; }
        public LinkedList<PlotEventEffect> Effects { get; set; }
        public LinkedList<PlotNextEvent> NextEvents { get; set; }

        private bool _computedHasSpecialEffects;
        private bool _hasSpecialEffects;

        public PlotEventTemplate()
        {
            Conditions = new LinkedList<PlotEventCondition>();
            AoeConditions = new LinkedList<PlotAoeCondition>();
            Effects = new LinkedList<PlotEventEffect>();
            NextEvents = new LinkedList<PlotNextEvent>();
            _computedHasSpecialEffects = false;
        }

        // TODO : Find better way of doing this. Tried doing it in the PlotManager, but SkillManager had not loaded at the time. Could use an event on SkillManager load like it is done for trade packs iirc.
        public bool HasSpecialEffects()
        {
            if (_computedHasSpecialEffects)
                return _hasSpecialEffects;
            
            _hasSpecialEffects = Effects
                .Select(eff =>
                    SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType))
                .OfType<SpecialEffect>()
                .Any();
            _computedHasSpecialEffects = true;

            return _hasSpecialEffects;
        }
    }
}

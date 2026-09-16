using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills.Plots;

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

    /// <summary>
    /// plot_events.target_update_method_param10 / _param11 (190 / 333 non-zero rows). Bound so the values
    /// are not lost, but nothing reads them: the shipped values are 1 and 30 in param10 and 30, 4, 28, 16,
    /// 2, 22 in param11 on Area and RandomArea events, and no reading of that pair is established by the
    /// server code, the client artifacts or the surrounding columns.
    /// </summary>
    public int TargetUpdateMethodParam10 { get; set; }
    public int TargetUpdateMethodParam11 { get; set; }

    /// <summary>Only units with no HP left may be picked; 12 events. See <see cref="Tree.PlotTargetRules"/>.</summary>
    public bool OnlyDieUnit { get; set; }

    /// <summary>Only the caster's own pet may be picked; 14 events (plot 3005, 사냥꾼 pet skills).</summary>
    public bool OnlyMyPet { get; set; }

    /// <summary>Only a pet's owner may be picked; 55 events (plot 3004 "pet uses a skill on its owner").</summary>
    public bool OnlyPetOwner { get; set; }

    /// <summary>Only the caster's own slaves may be picked; 55 events (plot 2706 대포 타겟, ship cannons).</summary>
    public bool OnlyMySlave { get; set; }
    public LinkedList<PlotEventCondition> Conditions { get; set; } = [];
    public LinkedList<PlotAoeCondition> AoeConditions { get; set; } = [];
    public LinkedList<PlotEventEffect> Effects { get; set; } = [];
    public LinkedList<PlotNextEvent> NextEvents { get; set; } = [];

    private bool _computedHasSpecialEffects = false;
    private bool _hasSpecialEffects;

    // TODO : Find better way of doing this. Tried doing it in the PlotManager, but SkillManager had not loaded at the time. Could use an event on SkillManager load like it is done for trade packs iirc.
    public bool HasSpecialEffects()
    {
        if (_computedHasSpecialEffects)
            return _hasSpecialEffects;

        _hasSpecialEffects = Effects
            .Select(eff =>
                SkillManager.Instance.GetEffectTemplate(eff.ActualId, eff.ActualType))
            .Where(eff => eff is SpecialEffect || eff is SkillControllerTemplate)
            .Any();
        _computedHasSpecialEffects = true;

        return _hasSpecialEffects;
    }
}

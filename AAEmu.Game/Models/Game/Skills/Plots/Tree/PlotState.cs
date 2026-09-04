using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public sealed class PlotClientEvent
{
    public ushort Tl { get; init; }
    public uint EventId { get; init; }
    public uint SkillId { get; init; }
    public PlotObject Caster { get; init; }
    public PlotObject Target { get; init; }
    public uint UnkId { get; init; }
    public ushort CastWire { get; init; }
    public byte Flag { get; init; }
    public byte TargetCount { get; init; }
    public ushort ChannelWire { get; init; }
}

public class PlotState(
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject,
    Skill skill)
{
    private bool _cancellationRequest = false;
    private bool _finishChanneling = false;
    public Dictionary<uint, int> Tickets { get; set; } = [];
    public int[] Variables { get; set; } = new int[12];
    /// <summary>
    /// Hit count from the latest plot target update (Area/RandomUnit/…). Consumed by
    /// SetVariable operation 12 on "타겟 수 체크" nodes.
    /// </summary>
    public int LastEffectedTargetCount { get; set; }
    public byte CombatDiceRoll { get; set; }
    public bool IsCasting { get; set; }
    public bool IsChanneling { get; set; }
    public PlotClientEvent LastClientEvent { get; set; }
    public DateTime LastIgnoredStopRefreshUtc { get; set; }

    public Skill ActiveSkill { get; set; } = skill;
    public Unit Caster { get; set; } = caster as Unit;
    public SkillCaster CasterCaster { get; set; } = casterCaster;
    public BaseUnit Target { get; set; } = target;
    public SkillCastTarget TargetCaster { get; set; } = targetCaster;
    public SkillObject SkillObject { get; set; } = skillObject;
    public List<(BaseUnit unit, uint buffId)> ChanneledBuffs { get; set; } = [];

    public Dictionary<uint, List<GameObject>> HitObjects { get; set; } = [];

    /// <summary>
    /// Radius (metres) of the area search that selected each unit, by unit ObjId.
    /// </summary>
    /// <remarks>
    /// Lets the plot's own Range gate (PlotCondition kind 11) know how far the selection legitimately
    /// reached. Backdraft picks its targets with aoe_shapes 19754 (r 9.7) and then re-checks them with
    /// Range 0..9, so a unit between 9.0 and 9.7m is selected, counted, and then silently dropped — while
    /// the client, which draws the telegraph from the shape, shows it well inside the cone.
    /// </remarks>
    public Dictionary<uint, float> AreaSelectionRadius { get; } = [];

    public bool CancellationRequested() => _cancellationRequest;
    public bool RequestCancellation() => _cancellationRequest = true;
    public bool ChannelingFinishRequested() => _finishChanneling;
    public bool FinishChanneling() => _finishChanneling = true;
    public bool PermitChanneling() => _finishChanneling = false;
}

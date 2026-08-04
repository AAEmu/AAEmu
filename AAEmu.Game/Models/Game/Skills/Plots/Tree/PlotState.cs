using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

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

    public Skill ActiveSkill { get; set; } = skill;
    public Unit Caster { get; set; } = caster as Unit;
    public SkillCaster CasterCaster { get; set; } = casterCaster;
    public BaseUnit Target { get; set; } = target;
    public SkillCastTarget TargetCaster { get; set; } = targetCaster;
    public SkillObject SkillObject { get; set; } = skillObject;
    public List<(BaseUnit unit, uint buffId)> ChanneledBuffs { get; set; } = [];

    public Dictionary<uint, List<GameObject>> HitObjects { get; set; } = [];

    public bool CancellationRequested() => _cancellationRequest;
    public bool RequestCancellation() => _cancellationRequest = true;
    public bool ChannelingFinishRequested() => _finishChanneling;
    public bool FinishChanneling() => _finishChanneling = true;
    public bool PermitChanneling() => _finishChanneling = false;
}

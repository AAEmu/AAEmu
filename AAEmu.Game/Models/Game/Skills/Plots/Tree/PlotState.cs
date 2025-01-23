using System.Collections.Generic;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotState
{
    private bool _cancellationRequest;
    private bool _finishChanneling;
    public Dictionary<uint, int> Tickets { get; set; }
    public int[] Variables { get; set; }
    public byte CombatDiceRoll { get; set; }
    public bool IsCasting { get; set; }
    public bool IsChanneling { get; set; }

    public Skill ActiveSkill { get; set; }
    public Unit Caster { get; set; }
    public SkillCaster CasterCaster { get; set; }
    public BaseUnit Target { get; set; }
    public SkillCastTarget TargetCaster { get; set; }
    public SkillObject SkillObject { get; set; }
    public List<(BaseUnit unit, uint buffId)> ChanneledBuffs { get; set; }

    public Dictionary<uint, List<GameObject>> HitObjects { get; set; }

    public PlotState(BaseUnit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject, Skill skill)
    {
        _cancellationRequest = false;
        _finishChanneling = false;

        Caster = caster as Unit;
        CasterCaster = casterCaster;
        Target = target;
        TargetCaster = targetCaster;
        SkillObject = skillObject;
        ActiveSkill = skill;

        HitObjects = [];
        Tickets = [];
        ChanneledBuffs = [];
        Variables = new int[12];
    }

    public bool CancellationRequested() => _cancellationRequest;
    public bool RequestCancellation() => _cancellationRequest = true;
    public bool ChannelingFinishRequested() => _finishChanneling;
    public bool FinishChanneling() => _finishChanneling = true;
    public bool PermitChanneling() => _finishChanneling = false;
}

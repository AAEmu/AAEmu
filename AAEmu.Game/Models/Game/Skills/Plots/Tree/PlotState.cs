using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Core.Packets.G2C;

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
    public uint[] TargetUnitIds { get; init; } = [];
    public ushort ChannelWire { get; init; }

    public SCPlotEventPacket ToPacket() => new(
        Tl, EventId, SkillId, Caster, Target, UnkId, CastWire, Flag, 0, TargetUnitIds,
        channelingTime: ChannelWire);
}

public class PlotState(
    BaseUnit caster,
    SkillCaster casterCaster,
    BaseUnit target,
    SkillCastTarget targetCaster,
    SkillObject skillObject,
    Skill skill,
    ushort castTlId = 0)
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

    /// <summary>
    /// Timeline id of the cast that owns this plot, captured on the thread that launched it.
    /// </summary>
    /// <remarks>
    /// Every plot packet the client receives — <c>SCPlotEvent</c>, <c>SCPlotEnded</c>, the casting and
    /// channeling stops — is matched by the client against the cast it started when the player pressed the
    /// key. The graph runs asynchronously and a cast-time skill clears <c>Skill.TlId</c> when its own cast
    /// ends, so reading the id back off the skill while an event fires yields zero and the client silently
    /// drops the whole graph, leaving it stuck on the last bar it was shown. The plot therefore carries the
    /// id it was started with.
    /// </remarks>
    public ushort CastTlId { get; } = castTlId;

    public Skill ActiveSkill { get; set; } = skill;
    public Unit Caster { get; set; } = caster as Unit;
    public SkillCaster CasterCaster { get; set; } = casterCaster;
    public BaseUnit Target { get; set; } = target;
    public SkillCastTarget TargetCaster { get; set; } = targetCaster;
    public SkillObject SkillObject { get; set; } = skillObject;
    public List<(BaseUnit unit, uint buffId)> ChanneledBuffs { get; set; } = [];

    public Dictionary<uint, List<GameObject>> HitObjects { get; set; } = [];

    /// <summary>
    /// Forgets every unit this plot has already hit, which is what a <c>hit_once</c> area search consults.
    /// </summary>
    /// <remarks>
    /// Driven by the TargetHistoryClearEffect plot effect (8 plot_effects rows, on the 발사 실패 "firing
    /// failed" branches of the gun and cannon plots). The history is kept per event and the clear is not:
    /// dropping all of it is what lets the retry loop hit the same unit again, which is the only thing those
    /// events ask for.
    /// </remarks>
    public void ClearHitHistory() => HitObjects.Clear();

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

    /// <summary>
    /// Start of the cast or channel bar the plot last advertised to the client, and how long that bar runs.
    /// </summary>
    /// <remarks>
    /// Plot condition kind 18 (casting_useable, 82 rows) asks which band of the bar the plot is in. The
    /// window is what the plot itself told the client — <c>SCPlotEventPacket</c> carries the cast and channel
    /// times — so the server and the cast bar the player sees agree on the percentage.
    /// </remarks>
    public DateTime CastWindowStartUtc { get; private set; }
    public int CastWindowMs { get; private set; }

    public void BeginCastWindow(int durationMs, DateTime nowUtc)
    {
        if (durationMs <= 0)
            return;
        CastWindowStartUtc = nowUtc;
        CastWindowMs = durationMs;
    }

    /// <summary>
    /// How far through the current cast or channel the plot is, or null when it never advertised one.
    /// </summary>
    public int? CastProgressPercent(DateTime nowUtc) =>
        CastWindowMs > 0
            ? PlotConditionRules.CastProgressPercent(CastWindowStartUtc, CastWindowMs, nowUtc)
            : null;

    public bool RequestCancellation() => _cancellationRequest = true;
    public bool ChannelingFinishRequested() => _finishChanneling;
    public bool FinishChanneling() => _finishChanneling = true;
    public bool PermitChanneling() => _finishChanneling = false;
}

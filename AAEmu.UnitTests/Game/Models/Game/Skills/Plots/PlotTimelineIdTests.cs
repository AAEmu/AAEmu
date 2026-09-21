using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Plots;

/// <summary>
/// A plot outlives the cast that started it: the graph runs on its own thread while a cast-time skill clears
/// <c>Skill.TlId</c> the moment its own cast ends. The client matches every plot packet — the event that
/// draws the bar, the casting and channeling stops, the end — against the timeline it started on the key
/// press, so the id has to travel with the plot instead of being read back off the skill.
/// </summary>
[NotInParallel]
public class PlotTimelineIdTests
{
    private const ushort LaunchTlId = 372;
    private const uint PlotSkillId = 11;

    private static Skill CreateClearedSkill()
    {
        // The state the plot is in for the rest of its life: EndSkill has already released and cleared the
        // id the cast was created with.
        var caster = new RecordingUnit { ObjId = 400 };
        return new Skill
        {
            Id = PlotSkillId,
            TlId = 0,
            Template = new SkillTemplate { Id = PlotSkillId, CooldownTime = 1000 }
        };
    }

    [Test]
    public async Task PlotState_KeepsTheLaunchTimelineIdWhileTheSkillHasNone()
    {
        var caster = new RecordingUnit { ObjId = 401 };
        var skill = CreateClearedSkill();
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill, LaunchTlId);

        await Assert.That(state.CastTlId).IsEqualTo(LaunchTlId);
        await Assert.That(state.ActiveSkill.TlId).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task PlotState_WithoutALaunchTimelineId_ReportsNone()
    {
        var caster = new RecordingUnit { ObjId = 402 };
        var skill = CreateClearedSkill();
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill);

        await Assert.That(state.CastTlId).IsEqualTo((ushort)0);
    }

    [Test]
    public async Task PlotEnd_TellsTheClientTheTimelineThePlotWasStartedOn()
    {
        var caster = new RecordingUnit { ObjId = 403 };
        var skill = CreateClearedSkill();
        var state = new PlotState(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill, LaunchTlId);
        caster.ActivePlotState = state;
        skill.ActivePlotState = state;

        PlotTree.EndPlotWithoutTree(state);

        var ended = caster.Packets.OfType<SCPlotEndedPacket>().SingleOrDefault();
        await Assert.That(ended).IsNotNull();

        var stream = new PacketStream();
        ended.Write(stream);
        stream.Rollback();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(LaunchTlId);
    }

    private sealed class RecordingUnit : Unit
    {
        public List<GamePacket> Packets { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}

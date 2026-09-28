using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// A cast that handed its end to its plot graph has to be closed by that graph, on the timeline the graph
/// was launched with, so the client's press runs from the Started to the end without a gap. A cast that kept
/// its end is left exactly as it was.
/// </summary>
/// <remarks>
/// Both cases go through <see cref="Plot.RunAsync"/> rather than the end method directly, because the two
/// ends are chosen there: a treeless plot only runs the end sequence when the plot owns the skill end, and
/// otherwise drops its state and leaves the cast to finish itself.
/// </remarks>
[NotInParallel]
public class PlayerAuthoredPlotEndTests
{
    private const uint SkillId = 1;
    private const uint PlotId = 7;

    private static Skill CreateSkill(bool playerAuthoredPlotCast) =>
        new()
        {
            Id = SkillId,
            PlayerAuthoredPlotCast = playerAuthoredPlotCast,
            Template = new SkillTemplate
            {
                Id = SkillId,
                CooldownTime = 1000,
                PlotOnly = false,
                // Tree left null: the treeless path, which is where the two ends are told apart. A skill
                // with a runnable tree reaches the same end through the tree.
                Plot = new Plot { Id = PlotId }
            }
        };

    /// <summary>Runs the graph the way <c>Skill.Use</c> does, and answers the id it was launched on.</summary>
    private static async Task<(RecordingUnit Caster, ushort LaunchTlId)> CastAndRunGraph(Skill skill, uint objId)
    {
        var caster = new RecordingUnit { ObjId = objId };
        skill.TlId = SkillTlIdManager.GetNextId(caster);
        var launchTlId = skill.TlId;
        await skill.Template.Plot.RunAsync(caster, new SkillCasterUnit(caster.ObjId), caster,
            new SkillCastUnitTarget(caster.ObjId), new SkillObject(), skill, launchTlId);
        return (caster, launchTlId);
    }

    private static ushort EncodeTimeline(GamePacket packet)
    {
        var stream = new PacketStream();
        packet.Write(stream);
        stream.Rollback();
        return stream.ReadUInt16();
    }

    [Test]
    public async Task GraphOwnedEnd_ClosesTheCastOnTheTimelineTheGraphRanOn()
    {
        var skill = CreateSkill(playerAuthoredPlotCast: true);
        var callbackFired = false;
        skill.Callback = () => callbackFired = true;

        var (caster, launchTlId) = await CastAndRunGraph(skill, 501);

        var skillEnded = caster.Packets.OfType<SCSkillEndedPacket>().SingleOrDefault();
        await Assert.That(skillEnded).IsNotNull();
        await Assert.That(EncodeTimeline(skillEnded)).IsEqualTo(launchTlId);
        // The cast end and the plot end name the same timeline, which is the one the Started named: the
        // client is told "your press ran to here" and nothing after it names a different cast.
        await Assert.That(EncodeTimeline(caster.Packets.OfType<SCPlotEndedPacket>().Single())).IsEqualTo(launchTlId);

        // The graph ran the end, so the id is handed back - the cast is not left holding an id the client
        // has just been told is closed.
        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
        await Assert.That(callbackFired).IsTrue();
        await Assert.That(caster.ActivePlotState).IsNull();
        await Assert.That(skill.ActivePlotState).IsNull();
    }

    [Test]
    public async Task CastOwnedEnd_KeepsSendingNoCastEndFromTheGraph()
    {
        var skill = CreateSkill(playerAuthoredPlotCast: false);
        var callbackFired = false;
        skill.Callback = () => callbackFired = true;

        var (caster, launchTlId) = await CastAndRunGraph(skill, 502);

        // The graph only drops its state here: the cast is still running and will end itself, so the
        // graph must not also tell the client the cast is over, and must not take the id away from the
        // cast that still has to end.
        await Assert.That(caster.Packets).IsEmpty();
        await Assert.That(skill.TlId).IsEqualTo(launchTlId);
        await Assert.That(callbackFired).IsFalse();
        await Assert.That(caster.ActivePlotState).IsNull();
        await Assert.That(skill.ActivePlotState).IsNull();
    }

    [Test]
    public async Task EndCastUnlessPlotOwnsIt_EndsTheCastOnlyWhenTheGraphDoesNotOwnIt()
    {
        var caster = new RecordingUnit { ObjId = 503 };
        var owned = CreateSkill(playerAuthoredPlotCast: true);
        var kept = CreateSkill(playerAuthoredPlotCast: false);
        owned.TlId = SkillTlIdManager.GetNextId(caster);
        kept.TlId = SkillTlIdManager.GetNextId(caster);
        var keptTlId = kept.TlId;

        owned.EndCastUnlessPlotOwnsIt(caster);
        await Assert.That(owned.TlId).IsNotEqualTo((ushort)0);
        await Assert.That(caster.Packets.OfType<SCSkillEndedPacket>()).IsEmpty();

        kept.EndCastUnlessPlotOwnsIt(caster);
        await Assert.That(kept.TlId).IsEqualTo((ushort)0);
        await Assert.That(EncodeTimeline(caster.Packets.OfType<SCSkillEndedPacket>().Single())).IsEqualTo(keptTlId);
    }

    private sealed class RecordingUnit : Unit
    {
        public List<GamePacket> Packets { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Packets.Add(packet);
    }
}

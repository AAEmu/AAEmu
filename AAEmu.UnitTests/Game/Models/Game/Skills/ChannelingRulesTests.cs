using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public class ChannelingRulesTests
{
    [Test]
    public async Task TickCount_IsTheNumberOfWholeIntervals()
    {
        // 10714 보호의 날개: 12,000 ms on a 1,000 ms tick.
        await Assert.That(ChannelingRules.TickCount(12000, 1000, 14)).IsEqualTo(12);
        // 11053 죽음의 속박: 7,000 ms on 1,000 ms with 14 mana per tick.
        await Assert.That(ChannelingRules.TickCount(7000, 1000, 14)).IsEqualTo(7);
        // A partial trailing interval is not a tick.
        await Assert.That(ChannelingRules.TickCount(7500, 1000, 14)).IsEqualTo(7);
    }

    [Test]
    public async Task TickCount_IsZeroWithoutAnIntervalOrACost()
    {
        // Seven of the eight non-plot channel skills drain nothing.
        await Assert.That(ChannelingRules.TickCount(12000, 1000, 0)).IsEqualTo(0);
        await Assert.That(ChannelingRules.TickCount(12000, 0, 14)).IsEqualTo(0);
        // 조각 만들기 (12949) has channeling_mana 100 but channeling_time 0: it never channels.
        await Assert.That(ChannelingRules.TickCount(0, 1000, 100)).IsEqualTo(0);
    }

    [Test]
    public async Task ManaPerTick_NeverTakesMoreThanTheUnitHolds()
    {
        await Assert.That(ChannelingRules.ManaPerTick(14, 100)).IsEqualTo(14);
        await Assert.That(ChannelingRules.ManaPerTick(14, 5)).IsEqualTo(5);
        await Assert.That(ChannelingRules.ManaPerTick(14, 0)).IsEqualTo(0);
    }

    [Test]
    public async Task StopsChannelOnNewCast_NeedsBothTheFlagAndARunningChannel()
    {
        await Assert.That(ChannelingRules.StopsChannelOnNewCast(true, true)).IsTrue();
        await Assert.That(ChannelingRules.StopsChannelOnNewCast(true, false)).IsFalse();
        await Assert.That(ChannelingRules.StopsChannelOnNewCast(false, true)).IsFalse();
    }

    [Test]
    public async Task EffectsOnlyLandWhenTheChannelRanOut()
    {
        await Assert.That(ChannelingRules.AppliesEffectsOnEnd(true)).IsTrue();
        await Assert.That(ChannelingRules.AppliesEffectsOnEnd(false)).IsFalse();
    }

    /// <summary>
    /// A channel that ran its full time applies its effects; one that was stopped does not.
    /// </summary>
    [Test]
    public async Task EndChanneling_AppliesTheSkillsEffects_OnlyOnNaturalCompletion()
    {
        var applied = 0;
        // Level matters: the effect loop compares the caster's level against the effect's 1..99 band.
        var caster = new Unit { ObjId = 300, Level = 60 };
        var target = new Unit { ObjId = 301 };
        var skill = new Skill
        {
            Id = 36620,
            Template = new SkillTemplate
            {
                Id = 36620,
                ChannelingTime = 6000,
                ChannelingTick = 1000,
                AbilityId = AbilityType.Death,
                TargetType = SkillTargetType.Self,
                // The DB defaults the level band to 1..99 and the application method to Target; a
                // hand-built row has to state both or the effect loop skips it.
                Effects =
                [
                    new SkillEffect
                    {
                        Template = new CountingEffect(() => applied++),
                        ApplicationMethod = SkillEffectApplicationMethod.Target,
                        StartLevel = 1,
                        EndLevel = 99,
                        Chance = 100
                    }
                ]
            }
        };
        skill.InitialTarget = target;
        var casterCaster = new SkillCasterUnit(caster.ObjId);
        var callbackFired = false;
        skill.Callback = () => callbackFired = true;

        // The cancelled path: CSStopCastingPacket, a stun or a death.
        skill.EndChanneling(caster, null, casterCaster);
        await Assert.That(applied).IsEqualTo(0);
        await Assert.That(callbackFired).IsTrue();

        // The natural path: the channel timer fired.
        applied = 0;
        callbackFired = false;
        skill = new Skill
        {
            Id = 36620,
            TlId = SkillTlIdManager.GetNextId(caster),
            Template = skill.Template
        };
        skill.InitialTarget = target;
        skill.Callback = () => callbackFired = true;
        var releasesBefore = ReleasesReported();

        skill.EndChanneling(caster, null, casterCaster, completedNaturally: true);

        await Assert.That(applied).IsEqualTo(1);
        await Assert.That(callbackFired).IsTrue();
        // The fire path ended the skill exactly once: the TlId is released and released only once.
        await Assert.That(skill.TlId).IsEqualTo((ushort)0);
        await Assert.That(ReleasesReported() - releasesBefore).IsEqualTo(1ul);
    }

    private static ulong ReleasesReported()
    {
        var status = SkillTlIdManager.ReportStatus();
        var marker = status.LastIndexOf("Releases ", StringComparison.Ordinal);
        return marker < 0 ? 0 : ulong.Parse(status[(marker + "Releases ".Length)..]);
    }

    /// <summary>An effect that counts applications and touches nothing else.</summary>
    private sealed class CountingEffect(Action onApply) : EffectTemplate
    {
        public override bool OnActionTime => false;

        public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
            => onApply();
    }
}

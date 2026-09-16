using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// A buff that grants skills (<c>buff_skills</c>, <c>buff_mount_skills</c>), swaps an action-bar entry
/// (<c>buff_swap_skills</c>) or hands over a passive (<c>buff_passive_buffs</c>) has to give all of it
/// back when it ends, by whichever route it ends — dispel, timeout, Exit, death — while leaving every
/// other skill alone.
/// </summary>
/// <remarks>
/// The content is faked (ids are invented); the shapes are the live ones: 실험형 날틀 1029 and eight
/// other glider buffs grant 날틀 접기 17657, 갈고리 사용 중 3624 grants 회전 착지 17618, and 23 stance
/// buffs swap 폭탄 발사 준비 35351 (망치 태세 18383 does it with priority 1 while the other three use 0).
/// The tables are loaded from the content DB by SkillManager.Load, which no unit test here exercises;
/// this covers what the loaded sets do once a buff starts and ends.
/// </remarks>
[NotInParallel]
public class BuffGrantedSkillsTests
{
    private const uint GliderBuffId = 91029;          // grants FoldGlider, removed on death
    private const uint SecondGliderBuffId = 93528;    // grants FoldGlider and GliderBoost
    private const uint HarpoonBuffId = 93624;         // grants HarpoonLanding
    private const uint StanceBuffLowId = 91882;       // swaps StanceOrigin -> FireballStance, priority 0
    private const uint StanceBuffHighId = 91883;      // swaps StanceOrigin -> FrostStance, priority 1
    private const uint PassiveGrantBuffId = 91712;    // buff_passive_buffs -> GrantedPassiveId
    private const uint FoldGliderSkillId = 97657;
    private const uint GliderBoostSkillId = 913435;
    private const uint HarpoonLandingSkillId = 97618;
    private const uint StanceOriginSkillId = 935351;
    private const uint FireballStanceSkillId = 934500;
    private const uint FrostStanceSkillId = 934501;
    private const uint LearnedSkillId = 91043;
    private const uint GrantedPassiveId = 9315;
    private const uint GrantedPassiveBuffId = 9581;
    private const uint SwapLowRowId = 10;
    private const uint SwapHighRowId = 9;

    private readonly List<byte[]> _sentPackets = [];
    private FieldInfo _skillManagerField;
    private FieldInfo _buffGameDataField;
    private object _previousSkillManager;
    private object _previousBuffGameData;
    private bool _previousZoneAuthority;

    [Before(Test)]
    public void InstallContentLookups()
    {
        _skillManagerField = SingletonField<SkillManager>();
        _buffGameDataField = SingletonField<BuffGameData>();
        _previousSkillManager = _skillManagerField.GetValue(null);
        _previousBuffGameData = _buffGameDataField.GetValue(null);
        // A buff start broadcasts SCBuffCreated; the zone relay it may also feed is off in tests.
        _previousZoneAuthority = WorldIntegration.ZoneAuthority;
        WorldIntegration.ZoneAuthority = false;
        _sentPackets.Clear();

        var manager = new SkillManager(
            Mock.Of<IAnimationManager>().Object,
            Mock.Of<IPlotManager>().Object);
        SetField(manager, "_skills", new Dictionary<uint, SkillTemplate>
        {
            [FoldGliderSkillId] = new SkillTemplate { Id = FoldGliderSkillId },
            [GliderBoostSkillId] = new SkillTemplate { Id = GliderBoostSkillId },
            [HarpoonLandingSkillId] = new SkillTemplate { Id = HarpoonLandingSkillId },
            [StanceOriginSkillId] = new SkillTemplate { Id = StanceOriginSkillId },
            [FireballStanceSkillId] = new SkillTemplate { Id = FireballStanceSkillId },
            [FrostStanceSkillId] = new SkillTemplate { Id = FrostStanceSkillId },
            [LearnedSkillId] = new SkillTemplate { Id = LearnedSkillId }
        });
        SetField(manager, "_buffs", new Dictionary<uint, BuffTemplate>
        {
            [GliderBuffId] = new BuffTemplate { Id = GliderBuffId, RemoveOnDeath = true },
            [SecondGliderBuffId] = new BuffTemplate { Id = SecondGliderBuffId },
            [HarpoonBuffId] = new BuffTemplate { Id = HarpoonBuffId },
            [StanceBuffLowId] = new BuffTemplate { Id = StanceBuffLowId },
            [StanceBuffHighId] = new BuffTemplate { Id = StanceBuffHighId },
            [PassiveGrantBuffId] = new BuffTemplate { Id = PassiveGrantBuffId },
            [GrantedPassiveBuffId] = new BuffTemplate { Id = GrantedPassiveBuffId }
        });
        SetField(manager, "_passiveBuffs", new Dictionary<uint, PassiveBuffTemplate>
        {
            [GrantedPassiveId] = new PassiveBuffTemplate { Id = GrantedPassiveId, BuffId = GrantedPassiveBuffId }
        });
        SetField(manager, "_buffGrants", new Dictionary<uint, BuffGrantSet>
        {
            [GliderBuffId] = new BuffGrantSet { GrantedSkills = [FoldGliderSkillId] },
            [SecondGliderBuffId] = new BuffGrantSet { GrantedSkills = [FoldGliderSkillId, GliderBoostSkillId] },
            [HarpoonBuffId] = new BuffGrantSet { GrantedSkills = [HarpoonLandingSkillId] },
            [StanceBuffLowId] = new BuffGrantSet
            {
                Swaps = [new BuffSkillSwap(SwapLowRowId, StanceBuffLowId, 0, StanceOriginSkillId, FireballStanceSkillId)]
            },
            [StanceBuffHighId] = new BuffGrantSet
            {
                Swaps = [new BuffSkillSwap(SwapHighRowId, StanceBuffHighId, 1, StanceOriginSkillId, FrostStanceSkillId)]
            },
            [PassiveGrantBuffId] = new BuffGrantSet { PassiveBuffIds = [GrantedPassiveId] }
        });
        _skillManagerField.SetValue(null, manager);

        var buffGameData = new BuffGameData();
        SetField(buffGameData, "_buffModifiers", new Dictionary<uint, List<BuffModifier>>());
        _buffGameDataField.SetValue(null, buffGameData);
    }

    [After(Test)]
    public void RestoreContentLookups()
    {
        _skillManagerField.SetValue(null, _previousSkillManager);
        _buffGameDataField.SetValue(null, _previousBuffGameData);
        WorldIntegration.ZoneAuthority = _previousZoneAuthority;
    }

    [Test]
    public async Task ApplyBuff_WithGrantedSkills_AddsExactlyThoseSkills()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = LearnedSkillId }, 1, packet: false);

        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));
        character.Buffs.AddBuff(CreateBuff(character, HarpoonBuffId));

        await Assert.That(character.Skills.TemporarySkills.Keys)
            .IsEquivalentTo(new[] { FoldGliderSkillId, HarpoonLandingSkillId });
        // Nothing else moved: the learned skill is still the only saved one.
        await Assert.That(character.Skills.Skills.ContainsKey(LearnedSkillId)).IsTrue();
        await Assert.That(character.Skills.Skills).HasCount().EqualTo(1);
        await Assert.That(character.Skills.LiveSkillIds()).Contains(LearnedSkillId);
    }

    [Test]
    public async Task ApplyBuff_WithTheSameGrantTwice_AddsItOnce()
    {
        var character = CreateCharacter();
        var buff = CreateBuff(character, GliderBuffId);
        character.Buffs.AddBuff(buff);

        // Start runs again on a refresh and on stack growth; the grant must not pile up.
        buff.Template.Start(character, character, buff);

        await Assert.That(character.Skills.TemporarySkills).HasCount().EqualTo(1);
        await Assert.That(character.Skills.TemporarySkills.ContainsKey(FoldGliderSkillId)).IsTrue();
    }

    [Test]
    public async Task BuffTimeout_ReleasesTheGrant()
    {
        var character = CreateCharacter();
        var buff = CreateBuff(character, GliderBuffId);
        character.Buffs.AddBuff(buff);
        await Assert.That(character.Skills.TemporarySkills.ContainsKey(FoldGliderSkillId)).IsTrue();

        buff.TimeOut();

        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
        await Assert.That(character.Buffs.CheckBuff(GliderBuffId)).IsFalse();
    }

    [Test]
    public async Task BuffExit_ReleasesTheGrant()
    {
        var character = CreateCharacter();
        var buff = CreateBuff(character, GliderBuffId);
        character.Buffs.AddBuff(buff);

        buff.Exit();

        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
    }

    [Test]
    public async Task RemoveBuff_ReleasesTheGrant_AndTheSecondDispelChangesNothing()
    {
        var character = CreateCharacter();
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));
        character.Buffs.AddBuff(CreateBuff(character, HarpoonBuffId));

        // Buffs.RemoveBuff dispels, removes, then lets SetInUse run Dispel a second time for the same
        // instance - the release has to be idempotent.
        character.Buffs.RemoveBuff(GliderBuffId, notifyZone: false);

        await Assert.That(character.Skills.TemporarySkills.Keys).IsEquivalentTo(new[] { HarpoonLandingSkillId });
        await Assert.That(character.Skills.TemporarySkills.ContainsKey(FoldGliderSkillId)).IsFalse();
    }

    [Test]
    public async Task RemoveEffectsOnDeath_ReleasesTheGrantOfABuffThatEndsOnDeath()
    {
        var character = CreateCharacter();
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));

        character.Buffs.RemoveEffectsOnDeath();

        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
        await Assert.That(character.Buffs.CheckBuff(GliderBuffId)).IsFalse();
    }

    [Test]
    public async Task TwoBuffsGrantingTheSameSkill_OneEnds_TheSkillStays()
    {
        var character = CreateCharacter();
        var first = CreateBuff(character, GliderBuffId);
        var second = CreateBuff(character, SecondGliderBuffId);
        character.Buffs.AddBuff(first);
        character.Buffs.AddBuff(second);
        await Assert.That(character.Skills.TemporarySkills.Keys)
            .IsEquivalentTo(new[] { FoldGliderSkillId, GliderBoostSkillId });

        first.Exit();

        // 날틀 접기 is still granted by the second buff; only the first buff's turn is over.
        await Assert.That(character.Skills.TemporarySkills.Keys)
            .IsEquivalentTo(new[] { FoldGliderSkillId, GliderBoostSkillId });

        second.Exit();

        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
    }

    [Test]
    public async Task EndingOneBuff_LeavesLearnedSkillsAndOtherGrantsAlone()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = LearnedSkillId }, 1, packet: false);
        var glider = CreateBuff(character, GliderBuffId);
        character.Buffs.AddBuff(glider);
        character.Buffs.AddBuff(CreateBuff(character, HarpoonBuffId));

        glider.Exit();

        await Assert.That(character.Skills.Skills.ContainsKey(LearnedSkillId)).IsTrue();
        await Assert.That(character.Skills.TemporarySkills.Keys).IsEquivalentTo(new[] { HarpoonLandingSkillId });
        await Assert.That(character.Skills.LiveSkillIds()).Contains(LearnedSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).Contains(HarpoonLandingSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(FoldGliderSkillId);
    }

    [Test]
    public async Task Swap_TakesTheOriginOffTheClientList_AndRestoresItOnEnd()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = StanceOriginSkillId }, 1, packet: false);
        var buff = CreateBuff(character, StanceBuffLowId);

        character.Buffs.AddBuff(buff);

        await Assert.That(character.Skills.LiveSkillIds()).Contains(FireballStanceSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(StanceOriginSkillId);
        await Assert.That(character.Skills.ReplacedSkillIds).Contains(StanceOriginSkillId);
        // The swap hides the learned skill; it does not unlearn it.
        await Assert.That(character.Skills.Skills.ContainsKey(StanceOriginSkillId)).IsTrue();
        await Assert.That(character.Skills.TemporarySkills.ContainsKey(StanceOriginSkillId)).IsFalse();

        buff.Exit();

        await Assert.That(character.Skills.LiveSkillIds()).Contains(StanceOriginSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(FireballStanceSkillId);
        await Assert.That(character.Skills.ReplacedSkillIds).IsEmpty();
        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
    }

    [Test]
    public async Task Swap_TwoBuffsOnTheSameOrigin_HighestPriorityWins_AndTheOtherTakesOverWhenItEnds()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = StanceOriginSkillId }, 1, packet: false);
        var low = CreateBuff(character, StanceBuffLowId);
        var high = CreateBuff(character, StanceBuffHighId);
        character.Buffs.AddBuff(low);
        character.Buffs.AddBuff(high);

        // 폭탄 발사 준비 is replaced by one skill at a time: the priority-1 row owns the entry.
        await Assert.That(character.Skills.LiveSkillIds()).Contains(FrostStanceSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(FireballStanceSkillId);

        high.Exit();

        await Assert.That(character.Skills.LiveSkillIds()).Contains(FireballStanceSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(FrostStanceSkillId);
        await Assert.That(character.Skills.LiveSkillIds()).DoesNotContain(StanceOriginSkillId);

        low.Exit();

        await Assert.That(character.Skills.LiveSkillIds()).Contains(StanceOriginSkillId);
        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
    }

    [Test]
    public async Task PassiveGrant_AppliesThroughThePassiveBuffPath_AndIsRemovedOnEnd()
    {
        var character = CreateCharacter();
        var buff = CreateBuff(character, PassiveGrantBuffId);

        character.Buffs.AddBuff(buff);

        // PassiveBuff.Apply is the path a learned passive uses, so the passive's own buff is up.
        await Assert.That(character.Buffs.CheckBuff(GrantedPassiveBuffId)).IsTrue();
        // It is not a learned passive: nothing for Save to write and no skill points spent.
        await Assert.That(character.Skills.PassiveBuffs).IsEmpty();

        buff.Exit();

        await Assert.That(character.Buffs.CheckBuff(GrantedPassiveBuffId)).IsFalse();
    }

    [Test]
    public async Task AlreadyLearnedSkill_IsNotHeldAsATemporaryGrant()
    {
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = FoldGliderSkillId }, 1, packet: false);
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));

        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
        await Assert.That(character.Skills.LiveSkillIds()).Contains(FoldGliderSkillId);

        character.Buffs.RemoveBuff(GliderBuffId, notifyZone: false);

        await Assert.That(character.Skills.LiveSkillIds()).Contains(FoldGliderSkillId);
    }

    [Test]
    public async Task TemporaryGrant_IsLiveForTheClientButNotInTheSavedSkillList()
    {
        var character = CreateCharacter();
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));

        // Live: the login skill list (SCUnitState) carries it and a cast is accepted.
        await Assert.That(character.Skills.LiveSkillIds()).Contains(FoldGliderSkillId);
        await Assert.That(character.Skills.HasSkill(FoldGliderSkillId)).IsTrue();
        // Not saved: CharacterSkills.Save writes Skills, and the grant never enters it.
        await Assert.That(character.Skills.Skills.ContainsKey(FoldGliderSkillId)).IsFalse();
    }

    [Test]
    public async Task TemporaryGrant_SaveWritesNoRow_SoNothingIsPersisted()
    {
        var character = CreateCharacter();
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));

        // Save dereferences its MySqlConnection only for a row it writes, so a null connection completes
        // exactly when there is nothing to write - which is the point: the grant is not persisted.
        Exception thrown = null;
        try
        {
            character.Skills.Save(null, null);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNull();
        await Assert.That(character.Skills.Skills).IsEmpty();
    }

    [Test]
    public async Task Save_WithALearnedSkillAndNoConnection_DoesThrow()
    {
        // The control for the test above: a skill in the saved list does reach the write loop.
        var character = CreateCharacter();
        character.Skills.AddSkill(new SkillTemplate { Id = LearnedSkillId }, 1, packet: false);

        Exception thrown = null;
        try
        {
            character.Skills.Save(null, null);
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await Assert.That(thrown).IsNotNull();
    }

    [Test]
    public async Task Grant_SendsSkillLearned_AndTheBuffRemovalIsWhatEndsIt()
    {
        var character = CreateCharacter();
        AttachConnection(character);
        var buff = CreateBuff(character, GliderBuffId);

        character.Buffs.AddBuff(buff);

        var learned = _sentPackets.Where(packet => Opcode(packet) == SCOffsets.SCSkillLearnedPacket).ToList();
        await Assert.That(learned).HasCount().EqualTo(1);
        await Assert.That(FirstUInt32(learned[0])).IsEqualTo(FoldGliderSkillId);

        _sentPackets.Clear();
        buff.Exit();

        // There is no "unlearn" opcode in SCOffsets; the client is told the buff is gone (0x0EC) and its
        // own buff/skill data drops the entry. The server state is what this PR is responsible for.
        await Assert.That(_sentPackets.Any(packet => Opcode(packet) == SCOffsets.SCSkillLearnedPacket)).IsFalse();
        await Assert.That(_sentPackets.Any(packet => Opcode(packet) == SCOffsets.SCBuffRemovedPacket)).IsTrue();
        await Assert.That(character.Skills.TemporarySkills).IsEmpty();
    }

    [Test]
    public async Task Grant_BeforeTheClientIsInTheWorld_SendsNothing()
    {
        // Character.Connection is set on character select, and passive buffs are applied at load: the
        // grant reaches the client through the login skill list instead.
        var character = CreateCharacter();
        character.Buffs.AddBuff(CreateBuff(character, GliderBuffId));

        await Assert.That(_sentPackets).IsEmpty();
        await Assert.That(character.Skills.LiveSkillIds()).Contains(FoldGliderSkillId);
    }

    private static Character CreateCharacter(uint id = 1)
    {
        var character = new Character(new UnitCustomModelParams()) { Id = id, ObjId = id, Name = $"Char{id}" };
        // Character.Skills is built by Character.Load, so a fresh one has to make it.
        character.Skills = new CharacterSkills(character);
        return character;
    }

    private void AttachConnection(Character character)
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        character.Connection = new GameConnection(session.Object) { ActiveChar = character };
    }

    /// <summary>
    /// The buff a real cast applies: same owner-as-caster shape the GM /buff command and the
    /// LoadActiveBuffs restore use. Skill stays null, which is what the wire writer expects for a buff
    /// that is not a skill toggle.
    /// </summary>
    private static Buff CreateBuff(Character owner, uint buffId) =>
        new(owner, owner, new SkillCasterUnit(owner.ObjId),
            SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow)
        {
            AbLevel = 1
        };

    /// <summary>
    /// Opcode of a captured packet. PacketMarshaler prefixes the encoded frame with its u16 length, and
    /// GameConnection encodes a level-1 packet as dd, level, crc, counter, typeId(u16), body while
    /// EncryptionActive is false, which is the state a test connection is in.
    /// </summary>
    private static ushort Opcode(byte[] packet) => (ushort)(packet[6] | (packet[7] << 8));

    private static uint FirstUInt32(byte[] packet) =>
        (uint)(packet[8] | (packet[9] << 8) | (packet[10] << 16) | (packet[11] << 24));

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}

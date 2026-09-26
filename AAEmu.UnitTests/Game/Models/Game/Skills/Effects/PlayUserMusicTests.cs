using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The solo play path end to end: a real instrument doodad the player is sitting at announces the
/// performance and carries the buff content gives it, a stranger at that doodad changes nothing,
/// a player with no instrument at all is refused, and a second play applies nothing twice. The
/// pause half ends the performance with the packet the neighbours need.
/// </summary>
// Loads InstrumentSoundGameData.Instance and installs the MusicManager/SkillManager singletons.
[NotInParallel]
public class PlayUserMusicTests : IDisposable
{
    private const uint PianoDoodad = 90001;
    private const uint PianoBuff = 91001;
    private const uint LuteItem = 90002;
    private const uint LuteBuff = 91002;
    private const uint SceneryDoodad = 90003;
    private const uint SceneryItem = 90004;
    private const uint OwnerId = 41;
    private const uint StrangerId = 42;
    private static readonly byte[] ValidMidi = [0x4D, 0x54, 0x68, 0x64];

    private SqliteConnection _connection;
    private object _previousMusicManager;
    private object _previousSkillManager;

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Before(Test)]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        Execute("CREATE TABLE enum_instrument_sound_kinds (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        Execute("CREATE TABLE buffs (id INTEGER PRIMARY KEY, name TEXT NOT NULL)");
        Execute("CREATE TABLE instrument_sounds (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, " +
                "midi INTEGER NOT NULL, kind_id INTEGER NOT NULL, buff_id INTEGER)");
        Execute("INSERT INTO enum_instrument_sound_kinds (id, name) VALUES (1, 'item'), (2, 'doodad')");
        Execute("INSERT INTO buffs (id, name) VALUES (91001, 'piano play'), (91002, 'lute play')");
        Execute("CREATE TABLE const_buff_types (id INTEGER PRIMARY KEY, name TEXT NOT NULL, buff_id INTEGER NOT NULL)");
        Execute("CREATE TABLE holdables (id INTEGER PRIMARY KEY, code TEXT NOT NULL)");
        Execute("CREATE TABLE item_weapons (id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL, holdable_id INTEGER NOT NULL)");
        Execute("INSERT INTO instrument_sounds (id, item_id, midi, kind_id, buff_id) VALUES " +
                "(1, 90001, 0, 2, 91001), " + // the placed piano
                "(2, 90002, 24, 1, 91002), " + // an instrument item
                "(3, 90003, 5, 9, NULL)"); // a row whose kind the enum does not name: dropped, loudly

        InstrumentSoundGameData.Instance.Load(_connection);

        // The play path asks the music manager for the MIDI and the pause path for the play-song
        // buffs; both managers answer from empty tables here.
        _previousMusicManager = SingletonField<MusicManager>().GetValue(null);
        _previousSkillManager = SingletonField<SkillManager>().GetValue(null);
        SingletonField<MusicManager>().SetValue(null,
            new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object));
        SingletonField<SkillManager>().SetValue(null,
            new SkillManager(Mock.Of<IAnimationManager>().Object, Mock.Of<IPlotManager>().Object));
    }

    [After(Test)]
    public void TearDown()
    {
        SingletonField<MusicManager>().SetValue(null, _previousMusicManager);
        SingletonField<SkillManager>().SetValue(null, _previousSkillManager);
        InstrumentSoundGameData.Instance.Load(_connection); // leave the singleton on this test's data
        _connection.Dispose();
    }

    public void Dispose() => _connection?.Dispose();

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Play(RecordingCharacter player) =>
        new PlayUserMusic().Execute(player, null, player, null, null, null, null, DateTime.UtcNow, 0, 0, 0, 0);

    private static void Pause(RecordingCharacter player, Skill skill = null) =>
        new PauseUserMusic().Execute(player, null, player, null, null, skill, null, DateTime.UtcNow, 0, 0, 0, 0);

    private static RecordingCharacter SeatedAt(uint ownerCharacterId, uint doodadTemplateId,
        DoodadOwnerType ownerType, bool cacheMidi = true)
    {
        var player = new RecordingCharacter { Id = ownerCharacterId, Name = $"Player{ownerCharacterId}" };
        DetachedInventory.Create(player);
        player.Buffs = new RecordingBuffs();
        player.Bonding = new BondDoodad(AttachPointKind.None, BondKind.BondInvalid, 0, 0);
        player.Bonding.SetOwner(new Doodad { TemplateId = doodadTemplateId, OwnerType = ownerType });
        if (cacheMidi)
            MusicManager.Instance.CacheMidi(player.Id, ValidMidi);
        return player;
    }

    [Test]
    public async Task PlayingThroughARealInstrumentDoodad_AnnouncesOnceAndAppliesTheInstrumentsBuffOnce()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;

        Play(player);
        Play(player); // the same performance asked for twice

        var announcements = player.Broadcasts.OfType<SCSendUserMusicPacket>().ToList();
        await Assert.That(announcements.Count).IsEqualTo(2);
        await Assert.That(buffs.AppliedBuffs).IsEquivalentTo(new List<uint> { PianoBuff });

        // The buff the content row carries is on them, so a third play resolves as already applied.
        await Assert.That(buffs.CheckBuff(PianoBuff)).IsTrue();
    }

    [Test]
    public async Task PlayingWithoutAValidMidiBlock_SendsNothingAndAppliesNoBuff()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System, cacheMidi: false);
        var buffs = (RecordingBuffs)player.Buffs;

        Play(player);

        await Assert.That(player.Broadcasts).IsEmpty();
        await Assert.That(buffs.AppliedBuffs).IsEmpty();
        await Assert.That(buffs.CheckBuff(PianoBuff)).IsFalse();
    }

    [Test]
    public async Task AStrangerAtAnOwnedInstrumentDoodad_IsRefusedWithNothingSent()
    {
        var player = SeatedAt(StrangerId, PianoDoodad, DoodadOwnerType.Character);
        player.Bonding.GetOwner().OwnerId = OwnerId;
        var buffs = (RecordingBuffs)player.Buffs;

        Play(player);

        await Assert.That(player.Broadcasts).IsEmpty();
        await Assert.That(buffs.AppliedBuffs).IsEmpty();
    }

    [Test]
    public async Task TheOwnerOfThatInstrumentDoodad_Plays()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.Character);
        player.Bonding.GetOwner().OwnerId = OwnerId;

        Play(player);

        await Assert.That(player.Broadcasts.OfType<SCSendUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(((RecordingBuffs)player.Buffs).AppliedBuffs).IsEquivalentTo(new List<uint> { PianoBuff });
    }

    [Test]
    public async Task AnInstrumentItemInTheMusicalSlot_PlaysWithoutADoodadToSitAt()
    {
        var player = new RecordingCharacter { Id = StrangerId, Name = "Held" };
        DetachedInventory.Create(player);
        player.Buffs = new RecordingBuffs();
        // Placed by hand: the container's own move path runs inventory acquisition, which pulls in
        // quest and item managers this test has no fixture for. The play path only reads the slot.
        var lute = new ItemMock(LuteItem, new ItemTemplate { Id = LuteItem, CategoryId = 80 })
        {
            Slot = (int)EquipmentItemSlot.Musical,
            SlotType = SlotType.Equipment,
            _holdingContainer = player.Inventory.Equipment,
        };
        player.Inventory.Equipment.Items.Add(lute);
        await Assert.That(player.Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Musical)).IsNotNull();
        MusicManager.Instance.CacheMidi(player.Id, ValidMidi);

        Play(player);

        await Assert.That(player.Broadcasts.OfType<SCSendUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(((RecordingBuffs)player.Buffs).AppliedBuffs).IsEquivalentTo(new List<uint> { LuteBuff });
    }

    [Test]
    public async Task NothingContentCallsAnInstrument_IsRefusedInsteadOfPlayed()
    {
        var player = SeatedAt(StrangerId, SceneryDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;

        Play(player);

        await Assert.That(player.Broadcasts).IsEmpty();
        await Assert.That(buffs.AppliedBuffs).IsEmpty();
    }

    [Test]
    public async Task ARowTheEnumCouldNotName_IsReportedByTheLoadAndThePlayIsRefused()
    {
        // Content says the row exists for this doodad, the load dropped it for a kind it can not
        // name, and the play therefore finds nothing to play through: loud at load, refused at play.
        await Assert.That(InstrumentSoundGameData.Instance.UnknownKindRowIds).IsEquivalentTo(new List<uint> { 3 });

        var player = SeatedAt(StrangerId, SceneryDoodad, DoodadOwnerType.System);
        Play(player);

        await Assert.That(player.Broadcasts).IsEmpty();
        await Assert.That(((RecordingBuffs)player.Buffs).AppliedBuffs).IsEmpty();
    }

    [Test]
    public async Task PlayingAgainAfterAPause_AnnouncesTheScoreAgainWithoutReapplyingTheBuff()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;

        Play(player);
        Pause(player);
        Play(player);

        await Assert.That(player.Broadcasts.OfType<SCSendUserMusicPacket>().Count()).IsEqualTo(2);
        await Assert.That(player.Broadcasts.OfType<SCPauseUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(buffs.AppliedBuffs).IsEquivalentTo(new List<uint> { PianoBuff });
    }

    [Test]
    public async Task PausingThePerformance_TellsTheNeighboursToStopAndKeepsTheBuff()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;
        Play(player);
        await Assert.That(player.Broadcasts.OfType<SCSendUserMusicPacket>().Count()).IsEqualTo(1);

        Pause(player);

        await Assert.That(player.Broadcasts.OfType<SCPauseUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(MusicManager.Instance.TryGetMidiCache(player.Id, out _)).IsTrue();
        await Assert.That(buffs.CheckBuff(PianoBuff)).IsTrue();
        await Assert.That(buffs.RemovedBuffs).IsEmpty();
    }

    [Test]
    public async Task StopPlayingSkill_IsAPauseAndDoesNotEndThePerformance()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;
        Play(player);

        Pause(player, new Skill { Id = SkillsEnum.StopPlaying });

        await Assert.That(player.Broadcasts.OfType<SCPauseUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(MusicManager.Instance.TryGetMidiCache(player.Id, out _)).IsTrue();
        await Assert.That(buffs.CheckBuff(PianoBuff)).IsTrue();
        await Assert.That(buffs.RemovedBuffs).IsEmpty();
    }

    [Test]
    public async Task ClientEndPacket_EndsThePerformanceAndDropsThePlayBuff()
    {
        var player = SeatedAt(OwnerId, PianoDoodad, DoodadOwnerType.System);
        var buffs = (RecordingBuffs)player.Buffs;
        Play(player);
        SetPlaySongBuff(PianoBuff);
        var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = player };
        var packet = new TestEndPacket();
        packet.Bind(connection);

        // Parsing must not touch the live world; the state change belongs to Execute.
        packet.Read(new PacketStream());
        await Assert.That(player.Broadcasts.OfType<SCPauseUserMusicPacket>()).IsEmpty();

        packet.Execute();

        await Assert.That(player.Broadcasts.OfType<SCPauseUserMusicPacket>().Count()).IsEqualTo(1);
        await Assert.That(MusicManager.Instance.TryGetMidiCache(player.Id, out _)).IsFalse();
        await Assert.That(buffs.RemovedBuffs).IsEquivalentTo(new List<uint> { PianoBuff });
        await Assert.That(buffs.CheckBuff(PianoBuff)).IsFalse();
    }

    private static void SetPlaySongBuff(uint buffId)
    {
        var manager = SingletonField<SkillManager>().GetValue(null);
        var field = typeof(SkillManager).GetField("_taggedBuffs", BindingFlags.Instance | BindingFlags.NonPublic);
        var tags = (Dictionary<uint, List<uint>>)field.GetValue(manager);
        tags[(uint)TagsEnum.PlaySong] = [buffId];
    }

    private sealed class TestEndPacket : CSPauseUserMusicPacket
    {
        public void Bind(GameConnection connection) => Connection = connection;
    }

    /// <summary>Captures what the play path broadcasts instead of pushing it at a socket.</summary>
    private sealed class RecordingCharacter : Character
    {
        public RecordingCharacter() : base(null)
        {
        }

        public List<GamePacket> Broadcasts { get; } = [];

        public override void BroadcastPacket(GamePacket packet, bool self) => Broadcasts.Add(packet);
    }

    /// <summary>
    /// Buffs as this path sees them: what was applied, what is on, what was taken off. The rest of
    /// the interface is not part of a solo performance, so it says so.
    /// </summary>
    private sealed class RecordingBuffs : IBuffs
    {
        private readonly HashSet<uint> _active = [];

        public List<uint> AppliedBuffs { get; } = [];
        public List<uint> RemovedBuffs { get; } = [];

        public void AddBuff(uint buffId, BaseUnit caster)
        {
            AppliedBuffs.Add(buffId);
            _active.Add(buffId);
        }

        public bool CheckBuff(uint id) => _active.Contains(id);

        public void RemoveBuff(uint buffId, bool notifyZone = true)
        {
            RemovedBuffs.Add(buffId);
            _active.Remove(buffId);
        }

        public void AddBuff(Buff buff, uint index = 0, int forcedDuration = 0) => throw new NotSupportedException();
        public bool CheckBuffImmune(BuffTemplate candidate, BaseUnit caster, Skill castingSkill = null) => false;
        public bool CheckBuffs(List<uint> ids) => ids != null && ids.All(_active.Contains);
        public bool CheckBuffTag(uint tagId) => false;
        public int GetStackCountByTagId(uint tagId) => 0;
        public int GetStackCountExceptTagId(uint tagId) => 0;
        public bool CheckDamageImmune(DamageType damageType) => false;
        public bool CheckKnockbackImmune() => false;
        public bool CheckManaBurnImmune() => false;
        public uint GetMissingRequiredBuffTag(BuffTemplate candidate) => 0;
        public void BroadcastBuffImmune(BaseUnit caster, CastAction castObj, SkillCaster casterObj) { }
        public IEnumerable<Buff> GetAbsorptionEffects() => [];
        public IEnumerable<Buff> GetDamageReflectionEffects() => [];
        public IEnumerable<Buff> GetManaShieldEffects() => [];
        public void GetAllBuffs(List<Buff> goodBuffs, List<Buff> badBuffs, List<Buff> hiddenBuffs, bool includeAllPassives) { }
        public int GetBuffCountById(uint buffId) => _active.Contains(buffId) ? 1 : 0;
        public IEnumerable<Buff> GetBuffsRequiring(uint buffId) => [];
        public Buff GetEffectByIndex(uint index) => null;
        public Buff GetEffectByTemplate(BuffTemplate template) => null;
        public Buff GetEffectFromBuffId(uint id) => null;
        public List<Buff> GetEffectsByType(Type effectType) => [];
        public bool HasEffectsMatchingCondition(Func<Buff, bool> predicate) => false;
        public void RemoveAllEffects() => _active.Clear();
        public void RemoveBuffs(BuffKind kind, int count, uint buffTagId = 0) => throw new NotSupportedException();
        public void RemoveBuffs(uint buffTagId, int count) => throw new NotSupportedException();
        public void RemoveEffect(Buff buff) => throw new NotSupportedException();
        public void RemoveEffect(uint index, bool notifyZone = true) => throw new NotSupportedException();
        public void RemoveEffect(uint templateId, uint skillId) => throw new NotSupportedException();
        public void RemoveEffectsOnDeath() { }
        public void RemoveStealth() { }
        public int SetToleranceStep(int toleranceId, int stepIndex) => 0;
        public void SetOwner(BaseUnit owner) { }
        public void TriggerRemoveOn(BuffRemoveOn on, uint value = 0) { }
        public void TimeoutBuffsFromSkill(uint skillId) { }
        public void SaveActiveBuffs(MySql.Data.MySqlClient.MySqlConnection connection,
            MySql.Data.MySqlClient.MySqlTransaction transaction, uint characterId) { }
        public void LoadActiveBuffs(Character character) { }
        public void CancelAllEffectTasks() { }
    }
}

using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The H-window difficulty request (CS 0x208): who may change a copy's difficulty, the transitions
/// that result from it, and the <see cref="SCSelectedInstanceDifficultPacket"/> the client is always
/// answered with — applied value on success, the copy's real state on a refusal.
/// </summary>
[NotInParallel]
public sealed class CSSelectInstanceDifficultPacketTests
{
    private const uint ZoneGroup = 966;
    private const uint CharacterId = 72_001;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly SingletonScope<IndunManager> _indun;
    private readonly IDisposable _worldManager;
    private object _originalDifficulties;

    public CSSelectInstanceDifficultPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "Picker",
            Connection = _connection,
        };
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _worldManager = TestDungeonWorld.InstallWorldManager();
        _indun = new SingletonScope<IndunManager>(new IndunManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object));
    }

    [Before(Test)]
    public void SeedDifficulties()
    {
        _originalDifficulties = DifficultiesField().GetValue(IndunGameData.Instance);
        DifficultiesField().SetValue(IndunGameData.Instance,
            new Dictionary<uint, HashSet<byte>> { [ZoneGroup] = [1, 2] });
    }

    [After(Test)]
    public void RestoreProcessState()
    {
        DifficultiesField().SetValue(IndunGameData.Instance, _originalDifficulties);
        _indun.Dispose();
        _worldManager.Dispose();
    }

    [Test]
    public async Task ReservedPicker_AppliesTheDifficultyAndIsAnsweredWithIt()
    {
        var (world, dungeon) = CreateCopy();
        await Assert.That(dungeon.BeginDifficultySelection(_character, null)).IsTrue();
        var applied = -1;
        world.Events.OnIndunDifficultChanged += (_, args) => applied = args.Difficult;

        Select(1);

        await AssertAnswer(difficult: 1, showUi: true);
        await Assert.That(dungeon.Difficult).IsEqualTo((byte)1);
        await Assert.That(applied).IsEqualTo(1);
    }

    [Test]
    public async Task ContentUnavailableDifficulty_IsRefusedAndAnsweredWithTheCopysState()
    {
        var (world, dungeon) = CreateCopy();
        dungeon.BeginDifficultySelection(_character, null);
        var events = 0;
        world.Events.OnIndunDifficultChanged += (_, _) => events++;

        // 9 is not one of this zone group's difficulties, so it must not reach the copy.
        Select(9);

        await AssertAnswer(difficult: 0, showUi: true);
        await Assert.That(dungeon.Difficult).IsNull();
        await Assert.That(events).IsEqualTo(0);
    }

    [Test]
    public async Task ConflictingSecondPick_IsRefusedAndAnsweredWithTheDifficultyAlreadyApplied()
    {
        var (world, dungeon) = CreateCopy();
        dungeon.BeginDifficultySelection(_character, null);
        await Assert.That(dungeon.SetDifficult((byte)1)).IsTrue();
        var events = 0;
        world.Events.OnIndunDifficultChanged += (_, _) => events++;

        // The first accepted selection owns the copy: a different pick is a refusal, reported with the
        // value the copy actually holds rather than the one that was refused.
        Select(2);

        await AssertAnswer(difficult: 1, showUi: true);
        await Assert.That(dungeon.Difficult).IsEqualTo((byte)1);
        await Assert.That(events).IsEqualTo(0);
    }

    [Test]
    public async Task PickBySomeoneElseWhileTheSelectionIsHeld_NeverTouchesTheCopy()
    {
        var (world, dungeon) = CreateCopy();
        var holder = new Character(new UnitCustomModelParams()) { Id = 99_001, ObjId = 99_001, Name = "Holder" };
        TestDungeonWorld.Enter(world, holder);
        await Assert.That(dungeon.BeginDifficultySelection(holder, null)).IsTrue();

        // The requester is in the copy but does not hold the selection: their pick stays a pending
        // pre-pick of their own and cannot decide this copy.
        Select(2);

        await AssertAnswer(difficult: 2, showUi: true);
        await Assert.That(dungeon.Difficult).IsNull();
        await Assert.That(dungeon.HasDifficultySelection(holder)).IsTrue();
        await Assert.That(IndunManager.Instance.TryTakeSelectedDifficult(CharacterId, out var prePick)).IsTrue();
        await Assert.That(prePick).IsEqualTo((byte)2);
    }

    [Test]
    public async Task PickOutsideAnyCopy_IsRememberedAsAPrePickAndAnsweredWithIt()
    {
        Select(3);

        await AssertAnswer(difficult: 3, showUi: true);
        await Assert.That(IndunManager.Instance.TryTakeSelectedDifficult(CharacterId, out var prePick)).IsTrue();
        await Assert.That(prePick).IsEqualTo((byte)3);
    }

    private (WorldInstance World, Dungeon Dungeon) CreateCopy()
    {
        var world = TestDungeonWorld.CreateWorld(960, 0, 280);
        var dungeon = TestDungeonWorld.CreateDungeon(
            new IndunZone { ZoneGroupId = ZoneGroup, InstanceCatalogId = 55 }, world);
        TestDungeonWorld.Enter(world, _character);
        return (world, dungeon);
    }

    private void Select(byte difficult) =>
        new CSSelectInstanceDifficultPacket { Connection = _connection }
            .Read(new PacketStream().Write(difficult).Write((byte)0));

    private async Task AssertAnswer(int difficult, bool showUi)
    {
        await Assert.That(_sent.Count).IsEqualTo(1);
        var (opcode, body) = SentPacket.Read(_sent[0]);
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCSelectedInstanceDifficultPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)difficult);
        await Assert.That(stream.ReadByte()).IsEqualTo(showUi ? (byte)1 : (byte)0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        _sent.Clear();
    }

    private static FieldInfo DifficultiesField() =>
        typeof(IndunGameData).GetField("_difficultiesByZoneGroup",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
}

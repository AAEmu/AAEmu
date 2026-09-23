using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// The zone-permission answer (CS 0x058) is a stub until an ask is opened and the state refresh
/// has a body. An answer sends nothing. The ask itself is still judged by the manager.
/// </summary>
[NotInParallel]
public sealed class CSAnswerZonePermissionPacketTests
{
    private const uint CharacterId = 71_001;
    private const uint ZoneGroupId = 955;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly SingletonScope<IndunManager> _indun;

    public CSAnswerZonePermissionPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "Asked",
            Connection = _connection,
        };
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _indun = new SingletonScope<IndunManager>(new IndunManager(
            Mock.Of<ITickManager>().Object,
            Mock.Of<IWorldManager>().Object,
            Mock.Of<IZoneManager>().Object,
            Mock.Of<ITeamManager>().Object));
    }

    [After(Test)]
    public void ReleaseManager() => _indun.Dispose();

    [Test]
    public async Task Answer_SendsNothingUntilAnAskIsOpened()
    {
        await Assert.That(IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId)).IsTrue();

        var mark = _sent.Count;
        Answer(1);
        Answer(0);
        Answer(7);

        await Assert.That(_sent.Count).IsEqualTo(mark);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsTrue();
    }

    [Test]
    public async Task AcceptedAnswer_SettlesTheAsk()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);

        await Assert.That(IndunManager.Instance.AnswerZonePermission(_character, 1))
            .IsEqualTo(ZonePermissionVerdict.Accepted);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task DeclinedAnswer_SettlesTheAsk()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);

        await Assert.That(IndunManager.Instance.AnswerZonePermission(_character, 0))
            .IsEqualTo(ZonePermissionVerdict.Declined);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task AnswerWithoutAnOpenAsk_ChangesNothing()
    {
        await Assert.That(IndunManager.Instance.AnswerZonePermission(_character, 1))
            .IsEqualTo(ZonePermissionVerdict.NoOpenAsk);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task MalformedAnswer_LeavesTheAskOpen()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);

        await Assert.That(IndunManager.Instance.AnswerZonePermission(_character, 7))
            .IsEqualTo(ZonePermissionVerdict.Malformed);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsTrue();
    }

    [Test]
    public async Task OpeningAnAskForNoZoneGroup_IsRefusedAndOpensNothing()
    {
        await Assert.That(IndunManager.Instance.OpenZonePermissionAsk(_character, 0)).IsFalse();
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsFalse();
    }

    [Test]
    public async Task AnswerWithoutAnActiveCharacter_SendsNothingAndDoesNotThrow()
    {
        IndunManager.Instance.OpenZonePermissionAsk(_character, ZoneGroupId);
        _connection.ActiveChar = null;

        var mark = _sent.Count;
        Answer(1);

        await Assert.That(_sent.Count).IsEqualTo(mark);
        await Assert.That(IndunManager.Instance.HasOpenZonePermissionAsk(CharacterId)).IsTrue();
    }

    private void Answer(byte value) =>
        new CSAnswerZonePermissionPacket { Connection = _connection }
            .Read(new PacketStream().Write(value));
}

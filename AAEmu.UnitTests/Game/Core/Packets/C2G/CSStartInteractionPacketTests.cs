using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

/// <summary>
/// Right-clicking a house sends <c>CSStartInteraction</c> with the house as the NPC. The client then
/// waits for <c>SCNpcInteractionSkillList</c> and acts on its first entry, so a house that is never
/// answered cannot be opened or built by right-click.
/// </summary>
[NotInParallel]
public sealed class CSStartInteractionPacketTests : IDisposable
{
    private const uint CharacterId = 81_001;
    private const uint HouseObjId = 902;

    private readonly List<byte[]> _sent = [];
    private readonly GameConnection _connection;
    private readonly WorldInstance _world;

    public CSStartInteractionPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sent.Add(bytes));
        _connection = new GameConnection(session.Object);
        var character = new Character(new UnitCustomModelParams())
        {
            Id = CharacterId,
            ObjId = CharacterId,
            Name = "Builder",
            Connection = _connection,
        };
        _connection.Characters.Add(character.Id, character);
        _connection.ActiveChar = character;

        _world = new WorldInstance(new WorldTemplate { Id = 1, Name = "house_interaction_test" }, 0, true, 1);
        SetParentWorld(character, _world);
    }

    public void Dispose() => _world.Dispose();

    [Test]
    public async Task FinishedHouse_IsAnsweredWithTheHousingInteraction()
    {
        AddHouse(hp: 1000);

        StartInteraction();

        // The body echoes the request (house, obj 0, extraInfo 1, pickId -1, mouse 2), then the list.
        var (opcode, body) = SentPacket.Read(_sent.Single());
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCNpcInteractionSkillListPacket);
        var stream = new PacketStream(body);
        await Assert.That(stream.ReadBc()).IsEqualTo(HouseObjId);
        await Assert.That(stream.ReadBc()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadInt32()).IsEqualTo(-1);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(SkillsEnum.HousingInteraction);
    }

    [Test]
    public async Task WreckedHouse_IsAnsweredWithAnEmptyList()
    {
        AddHouse(hp: 0);

        StartInteraction();

        var (opcode, body) = SentPacket.Read(_sent.Single());
        await Assert.That(opcode).IsEqualTo(SCOffsets.SCNpcInteractionSkillListPacket);
        var stream = new PacketStream(body);
        stream.ReadBc();
        stream.ReadBc();
        stream.ReadInt32();
        stream.ReadInt32();
        stream.ReadByte();
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
    }

    private void StartInteraction() =>
        new CSStartInteractionPacket { Connection = _connection }.Read(
            new PacketStream()
                .WriteBc(HouseObjId)
                .WriteBc(0)
                .Write(1)
                .Write(-1)
                .Write((byte)2)
                .Write(0));

    private void AddHouse(int hp)
    {
        var house = new House
        {
            Id = 1,
            ObjId = HouseObjId,
            TemplateId = 267,
            Template = new HousingTemplate { Id = 267, Hp = 1000, HousingBindingDoodad = [] },
        };
        house.CurrentStep = -1;
        house.Hp = hp;
        SetParentWorld(house, _world);
        _world.AddObject(house);
    }

    private static void SetParentWorld(GameObject obj, WorldInstance world) =>
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(obj, world);
}

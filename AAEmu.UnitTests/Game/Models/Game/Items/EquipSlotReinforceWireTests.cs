using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Models.Game.Items;

/// <summary>
/// Pins the three reinforcement packets' bodies. Each is a three byte object id, the equip slot, the
/// level and then — depending on the packet — the exp bar or the level effect.
/// </summary>
public class EquipSlotReinforceWireTests
{
    private const uint UnitId = 0x0001B2;

    [Test]
    public async Task Update_WritesBcSlotLevelAndExp()
    {
        var stream = new SCEquipSlotReinforceUpdatePacket(UnitId, 15, 4, 1600).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xB2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x00);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)15);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)4);
        await Assert.That(stream.ReadInt32()).IsEqualTo(1600);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Update_CarriesANegativeLevelForASlotThatWasNeverFed()
    {
        // Level 0 is the state of every slot before anything is fed, and the client reads it signed.
        var stream = new SCEquipSlotReinforceUpdatePacket(UnitId, 9, 0, 0).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xB2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x00);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)9);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)0);
        await Assert.That(stream.ReadInt32()).IsEqualTo(0);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task LevelEffectUpdate_WritesTheEffectInsteadOfTheExp()
    {
        var stream = new SCEquipSlotReinforceLevelEffectUpdatePacket(UnitId, 1, 3, 7).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xB2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x00);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)3);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(7u);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task LevelEffectDelete_StopsAfterTheLevel()
    {
        var stream = new SCEquipSlotReinforceLevelEffectDeletePacket(UnitId, 2, 5).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xB2);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0x00);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)2);
        await Assert.That(stream.ReadSByte()).IsEqualTo((sbyte)5);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task TheThreePacketsAreDistinctOpcodes()
    {
        var update = new SCEquipSlotReinforceUpdatePacket(UnitId, 0, 0, 0);
        var effectUpdate = new SCEquipSlotReinforceLevelEffectUpdatePacket(UnitId, 0, 0, 0);
        var effectDelete = new SCEquipSlotReinforceLevelEffectDeletePacket(UnitId, 0, 0);

        await Assert.That(update.TypeId).IsEqualTo((ushort)0x2C5);
        await Assert.That(effectUpdate.TypeId).IsEqualTo((ushort)0x2C6);
        await Assert.That(effectDelete.TypeId).IsEqualTo((ushort)0x2C7);
    }
}

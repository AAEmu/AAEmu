using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCRankerAppearancePacketTests
{
    [Test]
    public async Task Appearance_NamesTheHolderThenTheirLook()
    {
        var ranker = new Character(new UnitCustomModelParams()) { Id = 8, Level = 55 };

        var stream = new SCRankerAppearancePacket(3, ranker).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)3);   // the world the window asked about
        await Assert.That(stream.ReadInt64()).IsEqualTo(8L);       // the holder itself
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)55);  // their level
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);     // no equipment slot is occupied
        await Assert.That(stream.ReadUInt64()).IsEqualTo(0UL);     // nor carries synthesis effects
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0);   // a bare customisation block
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);      // no per-slot reinforcement levels
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);      // no per-slot artifact effects
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}

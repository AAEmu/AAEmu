using AAEmu.Commons.Network;

using AAEmu.Game.Models.Game.Indun;



namespace AAEmu.UnitTests.Game.Core.Packets.C2G;



public class CSAddInstanceVisitCountPacketTests

{

    [Test]

    public async Task RetailBody_IsSevenBytes_VisitTypeTypeType2()

    {

        var writer = new PacketStream();

        writer.Write((byte)IndunEntryRules.VisitTypeReset);

        writer.Write(42);

        writer.Write((short)51);

        var bytes = writer.GetBytes();

        await Assert.That(bytes.Length).IsEqualTo(7);



        var reader = new PacketStream(bytes);

        var visitType = reader.ReadSByte();

        var typeValue = reader.ReadInt32();

        var typeValue2 = reader.ReadInt16();

        await Assert.That(visitType).IsEqualTo(IndunEntryRules.VisitTypeReset);

        await Assert.That(typeValue).IsEqualTo(42);

        await Assert.That(typeValue2).IsEqualTo((short)51);

    }



    [Test]

    public async Task VisitTypeConstants_MatchClientIvt()

    {

        await Assert.That(IndunEntryRules.VisitTypeReset).IsEqualTo((sbyte)3);

        await Assert.That(IndunEntryRules.VisitTypePermit).IsEqualTo((sbyte)4);

    }

}



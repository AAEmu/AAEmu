using AAEmu.Commons.Network;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Validates Returns 10.0.2.13 quest context wire layout against the CN sniff sample
/// (sniff_decoded.txt SCQuestsPacket body for one active quest).
/// </summary>
public class QuestWireLayoutTests
{
    // From sniff: count=1 then quest body (without the leading count u32).
    // id=1700155 (s64), template=1112, status=3, 10 zero objectives via pish/pisc,
    // isCheckSet=0, three Bc(0)+u32(0), leftTime=-1, component=0, doodad=0, ...
    private static readonly byte[] SniffQuestBodyWithoutCount = Convert.FromHexString(
        "3bf1190000000000" + // s64 id 1700155
        "58040000" +         // u32 template 1112
        "03" +               // u8 status
        "0000000000" +       // pisc group1: pish + 4×u8
        "0000000000" +       // pisc group2
        "000000" +           // pisc group3: pish + 2×u8
        "00" +               // isCheckSet
        "000000" +           // Bc obj
        "00000000" +         // u32 type
        "000000" +           // Bc
        "000000" +           // Bc
        "ffffffff" +         // leftTime -1
        "00000000" +         // component
        "0000000000000000"   // doodad s64
    );

    [Test]
    public async Task WriteReturnsLayout_MatchesSniffPrefixThroughDoodad()
    {
        var stream = new PacketStream();
        // Mirror Quest.Write for the sniff field values (acceptTime is wall-clock — excluded).
        stream.Write((long)1700155);
        stream.Write(1112u);
        stream.Write((byte)3);
        stream.WritePisc(0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u);
        stream.Write(false);
        stream.WriteBc(0);
        stream.Write(0u);
        stream.WriteBc(0);
        stream.WriteBc(0);
        stream.Write(-1);
        stream.Write(0u);
        stream.Write(0L);

        var got = stream.GetBytes();
        await Assert.That(got.Length).IsEqualTo(SniffQuestBodyWithoutCount.Length);
        await Assert.That(got).IsEquivalentTo(SniffQuestBodyWithoutCount);
    }

    [Test]
    public async Task SniffScQuestsPacket_ParsesAsReturnsS64Layout()
    {
        // Full sniff body including count u32 = 1
        var full = Convert.FromHexString(
            "01000000" +
            "3bf11900000000005804000003" +
            "000000000000000000000000000000000000000000000000000000" +
            "ffffffff000000000000000000000000a35b456a0000000001ad090000");

        var count = BitConverter.ToUInt32(full, 0);
        var id = BitConverter.ToInt64(full, 4);
        var template = BitConverter.ToUInt32(full, 12);
        var status = full[16];

        await Assert.That(count).IsEqualTo(1u);
        await Assert.That(id).IsEqualTo(1700155L);
        await Assert.That(template).IsEqualTo(1112u);
        await Assert.That(status).IsEqualTo((byte)3);
    }
}

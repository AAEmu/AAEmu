using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCItemChangeMappingResultPacketTests
{
    private static byte[] Body(uint mappingGroupId, ItemChangeMappingResult result)
    {
        // TemplateId 0 writes only the id dword, which keeps this pin free of equip detail layout.
        var oldItem = new Item { TemplateId = 0 };
        var newItem = new Item { TemplateId = 0 };
        var stream = new PacketStream();
        new SCItemChangeMappingResultPacket(oldItem, newItem, mappingGroupId, result).Write(stream);
        return stream.GetBytes();
    }

    [Test]
    public async Task Body_EndsWithMappingGroupIdThenResult()
    {
        // The client looks the u32 up in item_change_mapping_groups before opening the result UI.
        // Pity bonus used to sit here; a miss means no popup at all.
        const uint groupId = 312;
        var body = Body(groupId, ItemChangeMappingResult.Fail);

        await Assert.That(body.Length).IsEqualTo(4 + 4 + 4 + 1);
        await Assert.That(BitConverter.ToUInt32(body, 8)).IsEqualTo(groupId);
        await Assert.That(body[^1]).IsEqualTo((byte)ItemChangeMappingResult.Fail);
    }

    [Test]
    public async Task SuccessResult_IsZero()
    {
        var body = Body(1, ItemChangeMappingResult.Success);
        await Assert.That(body[^1]).IsEqualTo((byte)0);
    }
}

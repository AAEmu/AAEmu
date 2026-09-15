using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.World.Core.Relay;

namespace AAEmu.UnitTests.Game.Models.Game.NPChar;

/// <summary>
/// The scale a mirrored NPC reports. The client sizes the model and its stride from the scale in
/// SCUnitState, so the spawner's own scale has to win over the template's - a spawner that scaled its
/// model down otherwise foot-slides its whole walk cycle.
/// </summary>
public class NpcMirrorScaleTests
{
    [Test]
    public async Task WithoutASpawnerScale_TheTemplateScaleStands()
    {
        var npc = CreateNpc(templateScale: 1.5f);

        await Assert.That(npc.ZoneSpawnScale).IsNull();
        await Assert.That(npc.Scale).IsEqualTo(1.5f);
    }

    [Test]
    public async Task ASpawnerScaleOverridesTheTemplate()
    {
        var npc = CreateNpc(templateScale: 1f);
        npc.ZoneSpawnScale = 0.8f;

        await Assert.That(npc.Scale).IsEqualTo(0.8f);
    }

    [Test]
    public async Task SpawnRecord_KeepsTheRawScaleSoZeroCanMeanUnset()
    {
        // A zero in the field must not become 1f: that would shrink every mirror of a template whose
        // model scale is not 1. Zero means "the spawner stated none", and the template stands.
        var stated = ZwSpawnNpcParser.TryParse(CreateSpawnBody(scale: 1.75f));
        var unstated = ZwSpawnNpcParser.TryParse(CreateSpawnBody(scale: 0f));

        await Assert.That(stated).IsNotNull();
        await Assert.That(stated!.Scale).IsEqualTo(1.75f);
        await Assert.That(unstated).IsNotNull();
        await Assert.That(unstated!.Scale).IsEqualTo(0f);
        await Assert.That(unstated.Scale > 0f).IsFalse();
    }

    private static Npc CreateNpc(float templateScale) =>
        new() { Template = new NpcTemplate { Id = 1, Scale = templateScale } };

    /// <summary>
    /// ZWSpawnNpc body: u32 sid, u32 sType, u8 mIdx, u8 pIdx, u16 tIdx, u32 templateId, u32 groupType,
    /// u32 groupId, u8 groupMemberIdx, f32 x, f32 y, f32 z, f32 zRot, f32 scale, then a zeroed tail.
    /// </summary>
    private static byte[] CreateSpawnBody(float scale) =>
        new PacketStream()
            .Write(0x00F00001u)   // spawner id
            .Write(1u)            // spawner type
            .Write((byte)0)       // member index
            .Write((byte)0)       // part index
            .Write((ushort)0)     // table index
            .Write(1001u)         // template id
            .Write(0u)            // group type
            .Write(0u)            // group id
            .Write((byte)0)       // group member index
            .Write(1000f)         // x
            .Write(2000f)         // y
            .Write(50f)           // z
            .Write(0f)            // z rotation
            .Write(scale)
            .Write(new byte[37])  // zeroed optional tail (78-byte ambient body)
            .GetBytes();
}

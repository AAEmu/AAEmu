using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// CS SkillCastExtra type 17, sent with the demolish skill (<c>is_demolish</c>, 12945) on a house.
/// </summary>
/// <remarks>
/// <c>X2House:Demolish(package, sealCount)</c> builds it (x2game <c>FUN_39998130</c>) and the
/// skill-object serializer writes it as <c>bool package</c> then <c>u32 sealCount</c>
/// (<c>FUN_39c7cfa0</c>, case 0x11). The maintenance window sends <c>(false, 0)</c> for a plain
/// demolition and <c>(true, total)</c> for Full Kit Demolition, where total is the certificate count
/// its dialog showed. Echoed as <see cref="SkillObjectType.None"/> on SC, like the other CS-only
/// extras.
/// </remarks>
public sealed class SkillObjectHouseDemolish : SkillObject
{
    /// <summary>Full Kit Demolition: the house is to come back as a completed design.</summary>
    public bool Package { get; set; }

    /// <summary>The certificates the client expects Full Kit Demolition to cost.</summary>
    public uint SealCount { get; set; }

    public override void Read(PacketStream stream)
    {
        Package = stream.ReadBoolean();
        SealCount = stream.ReadUInt32();
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(Package);
        stream.Write(SealCount);
        return stream;
    }
}

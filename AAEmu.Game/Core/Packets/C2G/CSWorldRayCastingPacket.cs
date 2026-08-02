using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// u64 x, u64 y, f32 z, vec3f direction, u32 id, bool isWaterLevelCasting,
/// bool isZoneServerCasting, bool isTextInfo.
/// </remarks>
public class CSWorldRayCastingPacket() : GamePacket(CSOffsets.CSWorldRayCastingPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var x = stream.ReadUInt64();
        var y = stream.ReadUInt64();
        var z = stream.ReadSingle();
        var dirX = stream.ReadSingle();
        var dirY = stream.ReadSingle();
        var dirZ = stream.ReadSingle();
        var id = stream.ReadUInt32();
        var isWaterLevelCasting = stream.ReadBoolean();
        _ = stream.ReadBoolean(); // isZoneServerCasting is a client-side routing decision.
        var isTextInfo = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character != null)
            WorldIntegration.RelayZoneRayCasting?.Invoke(
                character.ObjId, character.Id, x, y, z, dirX, dirY, dirZ, id,
                isWaterLevelCasting, isTextInfo);
    }
}

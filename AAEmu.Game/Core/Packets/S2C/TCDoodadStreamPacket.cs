using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.S2C;

/// <summary>
/// </summary>
public class TCDoodadStreamPacket(uint id, uint next, Doodad[] doodads) : StreamPacket(TCOffsets.TCDoodadStreamPacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(next);
        stream.Write(checked((uint)doodads.Length));
        foreach (var doodad in doodads)
        {
            stream.WriteBc(doodad.ObjId);
            stream.Write(doodad.TemplateId);
            stream.WritePosition(doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
            var (roll, pitch, yaw) = doodad.Transform.World.ToRollPitchYawShorts();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
            stream.Write(doodad.Scale);
            stream.Write(doodad.FuncGroupId); // doodad_func_groups Id
            // 10.0.2.13: the cell-stream doodad record ends at funcGroupId (32 bytes: objId(3) templateId(4)
            // pos(11) roll/pitch/yaw(6) scale(4) funcGroupId(4)). growing/plantTime are NOT part of the stream
            // record — a live official stream capture shows exactly 32-byte entries. The old TimeLeft+PlantTime
            // (12 B) made each entry too long, tripping the client's StreamClientImpl serializer size check.
        }

        return stream;
    }
}

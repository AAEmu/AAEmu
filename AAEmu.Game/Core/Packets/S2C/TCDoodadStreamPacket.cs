using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.S2C;

public class TCDoodadStreamPacket(int id, int next, Doodad[] doodads) : StreamPacket(TCOffsets.TCDoodadStreamPacket)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(next);
        stream.Write(doodads.Length);
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
            stream.Write(doodad.TimeLeft); // growing
            stream.Write(doodad.PlantTime); // plantTime
        }

        return stream;
    }
}

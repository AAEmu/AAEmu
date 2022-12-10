using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCDoodadStreamPacket : StreamPacket
    {
        private readonly int _id;
        private readonly int _next;
        private readonly Doodad[] _doodads;

        public TCDoodadStreamPacket(int id, int next, Doodad[] doodads) : base(TCOffsets.TCDoodadStreamPacket)
        {
            _id = id;
            _next = next;
            _doodads = doodads;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);             // id
            stream.Write(_next);           // next
            stream.Write(_doodads.Length); // count
            foreach (var doodad in _doodads)
            {
                stream.WriteBc(doodad.ObjId);    // bc
                stream.Write(doodad.TemplateId); // type
                stream.WritePosition(doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
                var (roll, pitch, yaw) = doodad.Transform.World.ToRollPitchYawShorts();
                stream.Write(roll);  // rotx
                stream.Write(pitch); // roty
                stream.Write(yaw);   // rotz
                stream.Write(doodad.Scale);
                stream.Write(doodad.FuncGroupId); // doodad_func_groups Id
                //stream.Write(doodad.TimeLeft); // growing
                //stream.Write(doodad.PlantTime); // plantTime
            }

            return stream;
        }
    }
}

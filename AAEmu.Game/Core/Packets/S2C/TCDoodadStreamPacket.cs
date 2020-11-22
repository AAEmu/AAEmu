using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.S2C
{
    public class TCDoodadStreamPacket : StreamPacket
    {
        private readonly int _id;
        private readonly int _next;
        private readonly Doodad[] _doodads;

        public TCDoodadStreamPacket(int id, int next, Doodad[] doodads) : base(0x02)
        {
            _id = id;
            _next = next;
            _doodads = doodads;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_next);
            stream.Write(_doodads.Length);
            foreach (var doodad in _doodads)
            {
                stream.WriteBc(doodad.ObjId);
                stream.Write(doodad.TemplateId);
                stream.WritePosition(doodad.Position.X, doodad.Position.Y, doodad.Position.Z);
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationX));
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationY));
                stream.Write(Helpers.ConvertRotation(doodad.Position.RotationZ));
                stream.Write(doodad.Scale);
                stream.Write(doodad.CurrentPhaseId); // doodad_func_groups Id
                stream.Write(doodad.TimeLeft); // growing
                stream.Write(doodad.PlantTime); // plantTime
            }

            return stream;
        }
    }
}

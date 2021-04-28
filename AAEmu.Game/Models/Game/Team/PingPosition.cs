using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Team
{
    public class PingPosition : PacketMarshaler
    {
        //public uint TeamId { get; set; }
        public bool HasPing { get; set; }
        public byte Flag { get; set; }
        public List<Point> Position1 { get; set; }
        public List<Point> Position2 { get; set; }
        public long[] Pisc1 { get; set; }
        public long[] Pisc2 { get; set; }
        public byte LineCount { get; set; }

        public PingPosition()
        {
            Position1 = new List<Point>() { new Point(), new Point(), new Point(), new Point(), new Point(), new Point() };
            Position2 = new List<Point>();
            Pisc1 = new long[] { 0, 0, 0, 0 };
            Pisc2 = new long[] { 0, 0 };
        }

        public override void Read(PacketStream stream)
        {
            HasPing = stream.ReadBoolean(); // setPingType
            Flag = stream.ReadByte();       // flag, added in 2.0.1.7

            for (var i = 0; i < 6; i++) // added in 2.0.1.7
            {
                var (x, y, z) = stream.ReadPosition(); // posXYZ
                Position1[i] = new Point(x, y, z);
            }

            Pisc1 = stream.ReadPisc(4); // added in 2.0.1.7
            Pisc2 = stream.ReadPisc(2); // added in 2.0.1.7

            LineCount = stream.ReadByte();   // lineCount, added in 2.0.1.7
            for (var i = 0; i < LineCount; i++)
            {
                var (x, y, z) = stream.ReadPosition(); // posXYZ
                Position2[i] = new Point(x, y, z);
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(HasPing);
            stream.Write(Flag);
            foreach (var pos in Position1) // max 6
            {
                stream.WritePosition(pos.X, pos.Y, pos.Z);
            }

            stream.WritePisc(Pisc1[0], Pisc1[1], Pisc1[2], Pisc1[3]);
            stream.WritePisc(Pisc2[0], Pisc2[1]);

            stream.Write(LineCount);
            if (Position2 != null && Position2.Count > 0)
            {
                foreach (var pos in Position2)
                {
                    stream.WritePosition(pos.X, pos.Y, pos.Z);
                }
            }
            return stream;
        }
    }
}

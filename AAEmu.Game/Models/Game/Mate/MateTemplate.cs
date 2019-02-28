using System.Collections.Generic;
using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Mate
{
    public class MateTemplate : PacketMarshaler
    {
        public uint Id { get; set; }
        public ulong ItemId { get; set; }
        public byte UserState { get; set; }
        public int Exp { get; set; }
        public int Mileage { get; set; }
        public uint SpawnDelayTime { get; set; }
        public List<uint> Skills { get; set; }

        public ushort TlId { get; set; }

        public MateTemplate()
        {
            Skills = new List<uint>();
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(ItemId);
            stream.Write(UserState);
            stream.Write(Exp);
            stream.Write(Mileage);
            stream.Write(SpawnDelayTime);

            // TODO - max 10 skills
            foreach (var skill in Skills)
            {
                stream.Write(skill);
            }

            for (var i = 0; i < 10 - Skills.Count; i++)
            {
                stream.Write(0);
            }

            return stream;
        }
    }
}

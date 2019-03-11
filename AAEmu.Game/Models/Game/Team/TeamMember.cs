using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils;
using Point = AAEmu.Game.Models.Game.World.Point;

namespace AAEmu.Game.Models.Game.Team
{
    public class TeamMember : PacketMarshaler
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public Race Race { get; set; }
        public byte Gender { get; set; }
        public byte Level { get; set; }
        public byte Role { get; set; }
        public uint UnitId { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public Point Position { get; set; }
        public byte Ability1 { get; set; }
        public byte Ability2 { get; set; }
        public byte Ability3 { get; set; }
        public bool IsOffline => Character == null;
        
        public Character Character { get; set; }

        public TeamMember(Character character)
        {
            Character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Name);
            stream.Write((byte)Race);
            stream.Write(Gender);
            stream.Write(Level);
            stream.Write(Role);
            stream.WriteBc(UnitId);
            return stream;
        }

        public PacketStream WritePerson(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write((ulong)0); // zi
            stream.Write(Level);
            stream.Write(Hp);
            stream.Write(MaxHp);
            stream.Write(Mp);
            stream.Write(MaxMp);
            stream.Write(Helpers.ConvertX(Position.X));
            stream.Write(Helpers.ConvertX(Position.Y));
            stream.Write(Helpers.ConvertZ(Position.Z));
            stream.Write(MathUtil.ConvertDirectionToDegree(Position.RotationZ)); // angZ
            stream.Write(Ability1);
            stream.Write(Ability2);
            stream.Write(Ability3);
            stream.Write(IsOffline);
            return stream;
        }
    }
}

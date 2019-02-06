using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game
{
    public class Friend : PacketMarshaler
    {
        public uint CharacterId { get; set; }
        public string Name { get; set; }
        public Race Race { get; set; }
        public byte Level { get; set; }
        public int Health { get; set; }
        public byte Ability1 { get; set; }
        public byte Ability2 { get; set; }
        public byte Ability3 { get; set; }
        public Point Position { get; set; }
        public bool InParty { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastWorldLeaveTime { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(CharacterId);
            stream.Write(Name);
            stream.Write((byte)Race);
            stream.Write(Level);
            stream.Write(Health);
            stream.Write(Ability1);
            stream.Write(Ability2);
            stream.Write(Ability3);
            stream.Write(Helpers.ConvertLongX(Position.X));
            stream.Write(Helpers.ConvertLongY(Position.Y));
            stream.Write(Position.Z);
            stream.Write(Position.ZoneId);
            stream.Write((uint)0); // type(id)
            stream.Write(InParty);
            stream.Write(IsOnline);
            stream.Write(LastWorldLeaveTime);
            return stream;
        }
    }
}

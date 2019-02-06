using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game
{
    public class Member : PacketMarshaler
    {
        public uint Id { get; set; } // TODO mb faction/family id?
        public uint Id2 { get; set; } // TODO mb characterId?
        public bool InParty { get; set; }
        public bool IsOnline { get; set; }
        public DateTime LastWorldLeaveTime { get; set; }
        public string Name { get; set; }
        public byte Level { get; set; }
        public int ZoneId { get; set; }
        public uint Id3 { get; set; } // TODO mb system faction.Id?
        public byte[] Abilities { get; set; } = {11, 11, 11};
        public byte Role { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public string Memo { get; set; }
        public DateTime TransferRequestedTime { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Id2);
            stream.Write(InParty);
            stream.Write(IsOnline);
            stream.Write(LastWorldLeaveTime);
            stream.Write(Name);
            stream.Write(Level);
            stream.Write(ZoneId);
            stream.Write(Id3);
            foreach (var ability in Abilities)
                stream.Write(ability);
            stream.Write(Role);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(Memo);
            stream.Write(TransferRequestedTime);
            return stream;
        }
    }
}

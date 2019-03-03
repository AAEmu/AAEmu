using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game
{
    public class ShipyardData : PacketMarshaler
    {
        public ulong Id { get; set; }
        public uint Id2 { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float zRot { get; set; }
        public int MoneyAmount { get; set; }
        public int Actions { get; set; }
        public int Type { get; set; }
        public int Type2 { get; set; }
        public string OwnerName { get; set; }
        public int Type3 { get; set; }
        public DateTime Spawned { get; set; }
        public uint ObjId { get; set; }
        public int Hp { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Id);
            stream.Write(Id2);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(zRot);
            stream.Write(MoneyAmount);
            stream.Write(Actions);
            stream.Write(Type);
            stream.Write(Type2);
            stream.Write(OwnerName);
            stream.Write(Type3);
            stream.Write(Spawned);
            stream.WriteBc(ObjId);
            stream.Write(Hp);
            return stream;
        }
    }
}

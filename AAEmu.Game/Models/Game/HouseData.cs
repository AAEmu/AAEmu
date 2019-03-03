using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game
{
    public class HouseData : PacketMarshaler
    {
        public ushort Tl { get; set; }
        public uint DbId { get; set; }
        public uint ObjId { get; set; }
        public uint TemplateId { get; set; }
        public int Ht { get; set; }
        public uint Unk2Id { get; set; }
        public uint Unk3Id { get; set; }
        public string Owner { get; set; }
        public uint Account { get; set; }
        public byte Permission { get; set; }
        public int AllStep { get; set; }
        public int CurStep { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public string House { get; set; }
        public bool AllowRecover { get; set; }
        public int MoneyAmount { get; set; }
        public uint Unk4Id { get; set; }
        public string SellToName { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Tl);
            stream.Write(DbId);
            stream.WriteBc(ObjId);
            stream.Write(TemplateId);
            stream.Write(Ht);
            stream.Write(Unk2Id);
            stream.Write(Unk3Id);
            stream.Write(Owner);
            stream.Write(Account);
            stream.Write(Permission);
            stream.Write(AllStep);
            stream.Write(CurStep);
            stream.Write(Helpers.ConvertLongX(X));
            stream.Write(Helpers.ConvertLongY(Y));
            stream.Write(Z);
            stream.Write(House);
            stream.Write(AllowRecover);
            stream.Write(MoneyAmount);
            stream.Write(Unk4Id);
            stream.Write(SellToName);
            return stream;
        }
    }
}

using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Shipyard;

public class ShipyardData : PacketMarshaler
{
    public ulong Id { get; set; }
    public uint TemplateId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float zRot { get; set; }
    public int MoneyAmount { get; set; }
    public int Actions { get; set; }
    public uint OriginItemId { get; set; } // Type
    public uint OwnerId { get; set; } // Type2S
    public string OwnerName { get; set; }
    public FactionsEnum FactionId { get; set; } // Type3
    public DateTime Spawned { get; set; }
    public uint ObjId { get; set; }
    public int Hp { get; set; }

    public int Step { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.WritePisc(TemplateId, Actions, OriginItemId, Hp);
        //stream.Write(TemplateId);
        stream.Write(Helpers.ConvertLongX(X));
        stream.Write(Helpers.ConvertLongY(Y));
        stream.Write(Z);
        stream.Write(zRot);
        stream.Write(MoneyAmount);
        //stream.Write(Actions);
        //stream.Write(OriginItemId);
        stream.Write(OwnerId);
        stream.Write(OwnerName);
        stream.Write((uint)FactionId);
        stream.Write(Spawned);
        stream.WriteBc(ObjId);
        //stream.Write(Hp);

        return stream;
    }
}

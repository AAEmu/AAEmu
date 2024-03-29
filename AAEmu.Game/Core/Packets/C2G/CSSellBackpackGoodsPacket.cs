﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSellBackpackGoodsPacket : GamePacket
{
    public CSSellBackpackGoodsPacket() : base(CSOffsets.CSSellBackpackGoodsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();

        var basePrice = SpecialtyManager.Instance.SellSpecialty(Connection.ActiveChar, objId);

        Logger.Warn($"CSSellBackpackGoods, ObjId: {objId}. BasePrice: {basePrice}");
    }
}

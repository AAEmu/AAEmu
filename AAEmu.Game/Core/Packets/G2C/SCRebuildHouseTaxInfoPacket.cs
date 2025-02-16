using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRebuildHouseTaxInfoPacket : GamePacket
{
    private readonly ushort _tl;
    private readonly int _dominionTaxRate;
    private readonly int _hostileTaxRate;
    private readonly int _moneyAmount;
    private readonly int _moneyAmount2;
    private readonly DateTime _due;
    private readonly bool _isAlreadyPaid;
    private readonly int _weeksWithoutPay;
    private readonly int _weeksPrepay;
    private readonly bool _isHeavyTaxHouse;

    public SCRebuildHouseTaxInfoPacket(ushort tl, int dominionTaxRate, int hostileTaxRate, int moneyAmount, int moneyAmount2, DateTime due, bool isAlreadyPaid,
        int weeksWithoutPay, int weeksPrepay,bool isHeavyTaxHouse) : base(SCOffsets.SCRebuildHouseTaxInfoPacket, 5)
    {
        _tl = tl;
        _dominionTaxRate = dominionTaxRate;
        _hostileTaxRate = hostileTaxRate;
        _moneyAmount = moneyAmount;
        _moneyAmount2 = moneyAmount2;
        _due = due;
        _isAlreadyPaid = isAlreadyPaid;
        _weeksWithoutPay = weeksWithoutPay;
        _weeksPrepay = weeksPrepay;
        _isHeavyTaxHouse = isHeavyTaxHouse;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write(0); // dr
        stream.Write(0); // count
        //for (int i = 0; i < count; i++)
        //{
        //    stream.Write(0);       // bt
        //    stream.Write((byte)0); // vt
        //    stream.Write(0f);      // pd
        //    stream.Write(0);       // wp
        //    stream.Write(0);       // dtr
        //}
        return stream;
    }
}

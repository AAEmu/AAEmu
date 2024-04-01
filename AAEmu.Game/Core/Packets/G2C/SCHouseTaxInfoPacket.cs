﻿using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseTaxInfoPacket : GamePacket
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

    public SCHouseTaxInfoPacket(ushort tl, int dominionTaxRate, int hostileTaxRate, int moneyAmount, int moneyAmount2, DateTime due, bool isAlreadyPaid,
        int weeksWithoutPay, int weeksPrepay,bool isHeavyTaxHouse) : base(SCOffsets.SCHouseTaxInfoPacket, 5)
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
        stream.Write(_dominionTaxRate);
        stream.Write(_hostileTaxRate);
        stream.Write(_moneyAmount);
        stream.Write(_moneyAmount2);
        stream.Write(_due);
        stream.Write(_isAlreadyPaid);
        stream.Write(_weeksWithoutPay);
        stream.Write(_weeksPrepay);
        stream.Write(_isHeavyTaxHouse);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCInitialConfigPacket : GamePacket
{
    private readonly string _host;
    private readonly int _count;
    private readonly uint _code;
    private readonly string _cashHost;
    private readonly string _securityHost;
    
    public SCInitialConfigPacket() : base(SCOffsets.SCInitialConfigPacket, 5)
    {
        _host = "aaemu.com";
        _count = 0;
        _code = 0; // TODO: code[count]
        _cashHost = "xlcash.xlgames.com";
        _securityHost = "cs.xlgames.com";
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_host); // host

        // siege -> (byte)fset[0] & 1 == 1
        // premium -> (byte)fset[0] & 0x10 == 0x10
        // levelLimit -> (byte)fset[1]
        // ranking -> (uint)fset[4] & 0x10 == 0x10
        // ingamecashshop -> (uint)fset[4] & 0x40 == 0x40
        // customsaveload -> (uint)fset[4] & 0x100 == 0x100
        // bm_mileage -> (uint)fset[4] & 0x800 == 0x800
        // itemSecure -> (uint)fset[4] & 0x2000 == 0x2000
        // secondpass -> (uint)fset[4] & 0x4000 == 0x4000
        // beautyshopBypass -> (uint)fset[4] & 0x100000 == 0x100000
        // ingameshopSecondpass -> (uint)fset[4] & 0x800000 == 0x800000
        // sensitiveOpeartion -> (uint)fset[4] & 0x4000000 == 0x4000000
        // taxItem -> (uint)fset[4] & 0x8000000 == 0x8000000
        // achievement -> (uint)fset[4] & 0x80000000 == 0x80000000
        // slave_customize -> (uint)fset[6] & 1 == 1
        // backpackProfitShare -> (byte)fset[7] & 1 == 1
        // mateLevelLimit -> (byte)fset[8]
        // dwarfWarborn -> (uint)fset[8] & 0x400 == 0x400
        // mailCoolTime -> (uint)fset[8] & 0x800 == 0x800
        // hudAuctionButton -> (uint)fset[8] & 0x20000 == 0x20000
        // auctionPostBuff -> (uint)fset[8] & 0x80000 == 0x80000
        // houseTaxPrepay -> (uint)fset[8] & 0x100000 == 0x100000

        FeaturesManager.Fsets.Write(stream);

        /*
            {
              [backpackProfitShare] => true
              [levelLimit] => 55
              [secondpass] => true
              [itemSecure] => true
              [customsaveload] => true
              [sensitiveOpeartion] => true
              [premium] => true
              [siege] => true
              [mateLevelLimit] => 50
              [houseTaxPrepay] => true
              [auctionPostBuff] => true
              [hudAuctionButton] => true
              [taxItem] => true
              [dwarfWarborn] => true
              [achievement] => true
              [bm_mileage] => true
              [mailCoolTime] => true
              [slave_customize] => true
              [beautyshopBypass] => true
              [ingamecashshop] => true
              [ingameshopSecondpass] => true
              [ranking] => true
            }
         */

        stream.Write(_count);    // count // candidatelist.lua
        if (_count > 0)
        {
            var i = 0;
            do
            {
                stream.Write(_code);    // code
                ++i;
            }
            while (i < _count);
        }

        /*
         * local retrieveCount = X2:GetCandidateOnceRetrieveCount()
         * x2ui\baselib
         */

        stream.Write(0); // initLp
        stream.Write(true); // canPlaceHouse
        stream.Write(true); // canPayTax
        stream.Write(true); // canUseAuction
        stream.Write(true); // canTrade
        stream.Write(true); // canSendMail
        stream.Write(true); // canUseBank
        stream.Write(true); // canUseCopper

        //stream.Write((byte)2); // secondPriceType
        /*
         * 0 - kr aapoint
         * 1 - ru aapoint
         * 2 - na loyalt token
         */
        stream.Write((byte)0); // secondPasswordMaxFailCount

        stream.Write(0u); // idleKickTime

        stream.Write(false); // enable
        stream.Write((byte)0); // pcbang
        stream.Write((byte)0); // premium
        stream.Write((byte)0); // maxch
        stream.Write((ushort)400); // honorPointDuringWarPercent
        stream.Write((byte)0); // uccver - 5
        stream.Write((byte)1); // memberType

        // added 5.0, 4.5
        stream.Write((float)256);    // bigModel
        stream.Write((byte)0);       // tmpMaxCharSlot
        stream.Write(_cashHost);     // cashHost
        stream.Write(_securityHost); // securityHost
        stream.Write(false);         // isDev

        return stream;
    }
}

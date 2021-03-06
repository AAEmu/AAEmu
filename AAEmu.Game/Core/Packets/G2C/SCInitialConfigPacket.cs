﻿using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInitialConfigPacket : GamePacket
    {
        public SCInitialConfigPacket() : base(SCOffsets.SCInitialConfigPacket, 5)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write("aaemu.local"); // TODO host - needs to be initiated in the config file

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

            // TODO 0x3E, 0x32, 0x0F, 0x0F, 0x79, 0x00, 0x33
            // TODO 0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03, 0xDE, 0xAE, 0x86, 0x3C, 0x0E, 0x02, 0xE6, 0x6F, 0xC7, 0xBB, 0x9B, 0x5D, 0x01, 0x00, 0x01

            stream.Write(0); // count
            /*
              (a2->Read->Int32)("count", this, 0);
               v3 = v2;
               if ( *v2 >= 100 )
               v3 = "d";
               result = *v3;
               v5 = 0;
               *v2 = result;
               if ( result > 0 )
               {
               v6 = (v2 + 1);
               do
               {
               result = (a2->Read->UInt32)("code", v6, 0);
               ++v5;
               v6 += 4;
               }
               while ( v5 < *v2 );
               }
             */
            stream.Write(50); // initLp
            stream.Write(false); // canPlaceHouse
            stream.Write(false); // canPayTax
            stream.Write(true); // canUseAuction
            stream.Write(true); // canTrade
            stream.Write(true); // canSendMail
            stream.Write(true); // canUseBank
            stream.Write(true); // canUseCopper

            //stream.Write((byte)0); // ingameShopVersion- missing in version 1.8
            //stream.Write((byte)2); // secondPriceType- missing in version 1.8
            /*
             * 0 - kr aapoint
             * 1 - ru aapoint
             * 2 - na loyalt token
            */

            stream.Write((byte)0);   // secondPasswordMaxFailCount
            stream.Write(0u);        // idleKickTime Uint32 in 1.7, Uint16 in 1.2 march
            stream.Write(false);     // enable - включает тестовый период запрещающий создание персонажей, added in 2.0
            stream.Write(true);      // pcbang, added in 2.0
            stream.Write(true);      // premium, added in 2.0
            stream.Write((byte)4);   // maxCh, added in 2.0
            stream.Write((ushort)5); // honorPointDuringWarPercent, added in 2.0

            return stream;
        }
    }
}

using System.Collections;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Features;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInitialConfigPacket : GamePacket
    {
        public SCInitialConfigPacket() : base(SCOffsets.SCInitialConfigPacket, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write("aaemu.local"); // host

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

            // 0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a
            // stream.Write(new byte[] {0x11, 0x37, 0x0F, 0x0F, 0x79, 0x69, 0xb3, 0x8d, 0x32, 0x0c, 0x1a}, true); // fset
            var fset = new FeatureSet();
            fset.Write(stream);

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

            stream.Write(50); // initLp
            stream.Write(false); // canPlaceHouse
            stream.Write(false); // canPayTax
            stream.Write(true); // canUseAuction
            stream.Write(true); // canTrade
            stream.Write(true); // canSendMail
            stream.Write(true); // canUseBank
            stream.Write(true); // canUseCopper

            stream.Write((byte)2); // secondPriceType
            /*
             * 0 - kr aapoint
             * 1 - ru aapoint
             * 2 - na loyalt token
             */
            stream.Write((byte)0); // secondPasswordMaxFailCount

            stream.Write((ushort)0); // idleKickTime

            return stream;
        }
    }
}

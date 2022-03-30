using System.Collections;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCInitialConfigPacket : GamePacket
    {
        private readonly string _host;
        private readonly int _count;
        private readonly uint _code;
        private readonly string _cashHost;
        private readonly string _securityHost;

        public SCInitialConfigPacket() : base(SCOffsets.SCInitialConfigPacket, 5)
        {
            _host = "archeagegame.com";
            _count = 0;
            _code = 0; // TODO: code[count]
            _cashHost = "xlcash.xlgames.com";
            _securityHost = "cs.xlgames.com";
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_host); // host

            // siege -> (byte)fset[0] & 1 == 1
            // itemSecure -> (uint)fset[4] & 0x2000 == 0x2000
            // secondpass -> (uint)fset[4] & 0x4000 == 0x4000
            // premium -> (byte)fset[0] & 0x10 == 0x10
            // ranking -> (uint)fset[4] & 0x10 == 0x10
            // slave_customize -> (uint)fset[6] & 1 == 1
            // bm_mileage -> (uint)fset[4] & 0x800 == 0x800
            // ingamecashshop -> (uint)fset[4] & 0x40 == 0x40
            // customsaveload -> (uint)fset[4] & 0x100 == 0x100
            // beautyshopBypass -> (uint)fset[4] & 0x100000 == 0x100000
            // ingameshopSecondpass -> (uint)fset[4] & 0x800000 == 0x800000
            // backpackProfitShare -> (byte)fset[7] & 1 == 1
            // sensitiveOpeartion -> (uint)fset[4] & 0x4000000 == 0x4000000
            // taxItem -> (uint)fset[4] & 0x8000000 == 0x8000000
            // achievement -> (uint)fset[4] & 0x80000000 == 0x80000000
            // dwarfWarborn -> (uint)fset[8] & 0x400 == 0x400
            // mailCoolTime -> (uint)fset[8] & 0x800 == 0x800
            // levelLimit -> (byte)fset[1]
            // mateLevelLimit -> (byte)fset[8]
            // hudAuctionButton -> (uint)fset[8] & 0x2000000 == 0x2000000
            // auctionPostBuff -> (uint)fset[8] & 0x8000000 == 0x8000000
            // itemRepairInBag -> (uint)fset[8] & 0x10000000 == 0x10000000
            // petOnlyEnchantStone -> (uint)fset[8] & 0x20000000 == 0x20000000
            // questNpcTag -> (uint)fset[8] & 0x40000000 == 0x40000000
            // houseTaxPrepay -> (uint)fset[8] & 0x80000000 == 0x80000000
            // hudBattleFieldButton -> (uint)fset[12] & 4 == 4
            // hudMailBoxButton -> (uint)fset[12] & 8 == 8
            // fastQuestChatBubble -> fset[12] & 0x10 == 0x10
            // todayAssignment -> fset[12] & 0x20 == 0x20
            // forbidTransferChar -> fset[12] & 0x40 == 0x40
            // targetEquipmentWnd -> fset[12] & 0x80 == 0x80
            // indunPortal -> fset[12] & 0x400 == 0x400
            // indunDailyLimit -> fset[12] & 0x4000 == 0x4000
            // rebuildHouse -> fset[12] & 0x8000 == 0x8000
            // useTGOS -> fset[14] & 1 == 1
            // forcePopupTGOS -> fset[12] & 0x20000 == 0x20000
            // newNameTag -> fset[12] & 0x40000 == 0x40000
            // reportSpammer -> fset[12] & 0x80000 == 0x80000
            // hero -> fset[12] & 0x100000 == 0x100000
            // marketPrice -> fset[12] & 0x200000 == 0x200000
            // buyPremiuminSelChar -> fset[12] & 0x800000 == 0x800000
            // freeResurrectionInPlace -> fset[16] & 0x800 == 0x800
            // useSavedAbilities -> fset[16] & 0x8000 == 0x8000
            // itemEvolving -> fset[16] & 0x4000 == 0x4000
            // itemEvolvingReRoll -> fset[20] & 0x10 == 0x10
            // rankingRenewal -> fset[18] & 1 == 1
            // hudAuctionBuff -> fset[16] & 0x20000 == 0x20000
            // expeditionRecruit -> fset[16] & 0x80000 == 0x80000
            // expeditionLevel -> fset[16] & 0x2000 == 0x2000
            // uiAvi -> fset[16] & 0x100000 == 0x100000
            // expeditionWar -> fset[16] & 0x400 == 0x400
            // shopOnUI -> fset[16] & 0x200000 == 0x200000
            // itemLookConvertInBag -> fset[16] & 0x400000 == 0x400000
            // newReportBaduser -> fset[16] & 0x800000 == 0x800000
            // accountAttendance -> fset[16] & 0x40000 == 0x40000
            // expeditionSummon -> fset[16] & 0x2000000 == 0x2000000
            // heroBonus -> fset[16] & 0x4000000 == 0x4000000
            // aaPoint -> fset[4] & 0x1000 == 0x1000
            // hairTwoTone -> fset[16] & 0x40000000 == 0x40000000
            // hudBattleFieldBuff -> fset[16] & 0x80000000 == 0x80000000
            // freeLpRaise -> fset[4] & 0x200000 == 0x200000
            // expeditionRank -> fset[20] & 1 == 1
            // mateTypeSummon -> fset[20] & 2 == 2
            // permissionZone -> fset[20] & 4 == 4
            // lootGacha -> fset[20] & 8 == 8
            // ranking_myworld_only -> fset[20] & 0x40 == 0x40
            // eloRating -> fset[20] & 0x80 == 0x80
            // nationMemberLimit -> fset[20] & 0x100 == 0x100
            // packageDemolish -> fset[20] & 0x400 == 0x400
            // itemGuide -> fset[20] & 0x1000 == 0x1000
            // restrictFollow -> fset[20] & 0x2000 == 0x2000
            // socketExtract -> fset[20] & 0x4000 == 0x4000
            // itemlookExtract -> fset[20] & 0x8000 == 0x8000
            // useCharacterListPage -> fset[22] & 1 == 1
            // renameExpeditionByItem -> fset[20] & 0x20000 == 0x20000
            // expeditionImmigration -> fset[20] & 0x40000 == 0x40000
            // highAbility -> fset[20] & 0x80000 == 0x80000
            // eventWebLink -> fset[20] & 0x100000 == 0x100000
            // blessuthstin -> fset[20] & 0x200000 == 0x200000
            // vehicleZoneSimulation -> fset[20] & 0x400000 == 0x400000
            // itemSmelting -> fset[20] & 0x800000 == 0x800000
            // characterInfoLivingPoint -> fset[24] & 1 == 1
            // useForceAttack -> fset[24] & 2 == 2
            // specialtyTradeGoods -> fset[24] & 4 == 4
            // reportBadWordUser -> fset[24] & 8 == 8
            // residentweblink -> fset[24] & 0x20 == 0x20

            //stream.Write(new byte[] { 0x7F, 0x37, 0x34, 0x0F, 0x79, 0x08, 0x7D, 0xCB, 0x37, 0x65, 0x03, 0xDE, 0xAE, 0x86, 0x3C, 0x0E, 0x02, 0xE6, 0x6F, 0xC7, 0xBB, 0x9B, 0x5D, 0x01, 0x00, 0x01 }, true); // fset
            //stream.Write(new byte[] { 0x7E, 0x37, 0x0F, 0x0F, 0x79, 0x08, 0xFD, 0x65, 0x37, 0x65, 0x03, 0xD6, 0xE2, 0xD6, 0x78, 0x1E, 0x02, 0xF6, 0xF7, 0xE7, 0xDD, 0xB6, 0x8E, 0x04, 0x8B, 0xF5, 0x93, 0x05 }, true); // fset 5750
            //stream.Write(new byte[] { 0x3F, 0x37, 0x0F, 0x0F, 0x79, 0x08, 0xFD, 0xB2, 0x37, 0x67, 0x03, 0xD6, 0xE3, 0xD6, 0x78, 0x1E, 0x02, 0xF6, 0xF7, 0xE3, 0xDF, 0xB6, 0x8D, 0x04, 0x45, 0xF1, 0x93, 0x00 }, true); // fset 6070
            stream.Write(new byte[] { 0x7F, 0x37, 0x0F, 0x0F, 0x79, 0x88, 0xED, 0xB2, 0x37, 0x67, 0x03, 0xD6, 0xE2, 0xD6, 0x78, 0x1E, 0x01, 0xF6, 0xFB, 0xF1, 0xDF, 0xB6, 0xB1, 0x04, 0x74, 0x5F, 0x23, 0x12, 0x00, 0x00 }, true); // fset 7533
            stream.Write(_count);    // count
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

            // TODO config
            stream.Write(0);    // initLp 50 in 5750, 0 in 6070
            stream.Write(false); // canPlaceHouse
            stream.Write(false); // canPayTax
            stream.Write(true);  // canUseAuction
            stream.Write(true);  // canTrade
            stream.Write(true);  // canSendMail
            stream.Write(true);  // canUseBank
            stream.Write(true);  // canUseCopper

            //stream.Write((byte)2); // secondPriceType
            /*
             * 0 - kr aapoint
             * 1 - ru aapoint
             * 2 - na loyalt token
             */
            stream.Write((byte)0); // secondPasswordMaxFailCount

            stream.Write(0); // idleKickTime

            stream.Write(false);   // enable
            stream.Write((byte)0); // pcbang
            stream.Write((byte)0); // premium
            stream.Write((byte)0); // maxCh
            stream.Write((ushort)400); // honorPointDuringWarPercent
            stream.Write((byte)9); // uccver 6 in 5750, 8 in 6070, 9 in 7533
            stream.Write((byte)1); // memberType

            // added 5.7.5.0
            stream.Write((float)256);    // bigModel
            stream.Write((byte)0);       // tmpMaxCharSlot 0 in 5750, 2 in 6070
            stream.Write(_cashHost);     // cashHost
            stream.Write(_securityHost); // securityHost
            stream.Write(false);         // isDev
            stream.Write(1u);         // premiumConfigType
            stream.Write(1u);         // specificWorldDivisionIds
            stream.Write(0x00000FA1); // ?

            return stream;
        }
    }
}

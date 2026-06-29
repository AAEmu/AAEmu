using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_CHARACTER_STATE (opcode 108) — sent on SelectCharacter to bridge the lobby into the world.
// Wire layout mirrors CharacterStatePacket::SerializeBody (x2game-dev_dedicate.dll sub_39C18310):
//   iid u32, guid str(16 raw), rwd u32, srwd u32, then the character body sub_39B18F50, which is the
//   SAME lobby-char record as SC_PACKET_CHARACTER_LIST (CharacterListPacket_WriteLobbyChar 0x39B165E0 ==
//   Character.WriteLobby1013) followed by the in-world "state" tail.
// vtbl serializer widths (verified by diffing sub_39B165E0 against the working WriteLobby1013):
//   +120 i64, +128 u32, +144 u8, +152 i64, +160 u32, +168 i16, +248 bool, +464(count=16) = 16 raw bytes.
public class SCCharacterStatePacket(Character character) : GamePacket(SCOffsets.SCCharacterStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Wrapper (sub_39C18310)
        stream.Write((uint)character.Transform.InstanceId); // iid
        stream.Write(new byte[16]);                         // guid (fixed 16-byte field, not length-prefixed)
        stream.Write(0u);                                   // rwd
        stream.Write(0u);                                   // srwd

        // Character body — lobby record (CharacterListPacket_WriteLobbyChar 0x39B165E0)
        character.WriteLobby1013(stream);

        // State tail (sub_39B18F50)
        stream.Write(0f);                                   // angles.x
        stream.Write(0f);                                   // angles.y
        stream.Write(0f);                                   // angles.z (12-byte "angles" block, vtbl+184)

        stream.Write((uint)character.Experience);           // exp
        stream.Write(0L);                                   // heirExp
        stream.Write((uint)character.RecoverableExp);       // recoverableExp
        stream.Write(0u);                                   // penaltiedExp
        stream.Write(0u);                                   // returnDistrictId
        stream.Write(0u);                                   // returnDistrict.type   (optional group, always present)
        stream.Write(0u);                                   // resurrectionDistrict.type (optional group, always present)

        for (var i = 0; i < 30; i++)
            stream.Write(0u);                               // abilityExp[30]

        stream.Write(0u);                                   // totalSentMail
        stream.Write(0u);                                   // totalMail
        stream.Write(0u);                                   // totalMiaMail
        stream.Write(0u);                                   // totalCommercialMail
        stream.Write(character.Mails.UnreadMailCount.Received);            // unreadMail
        stream.Write(character.Mails.UnreadMailCount.MiaReceived);         // unreadMiaMail
        stream.Write(character.Mails.UnreadMailCount.CommercialReceived);  // unreadCommercialMail

        stream.Write((byte)character.NumInventorySlots);    // numInvenSlots (u8)
        stream.Write((short)character.NumBankSlots);        // numBankSlots  (i16)

        stream.Write(character.Money);                      // moneyAmount (inventory)
        stream.Write(character.Money2);                     // moneyAmount (bank)
        stream.Write(0L);                                   // moneyAmount
        stream.Write(0L);                                   // moneyAmount

        stream.Write((byte)(character.AutoUseAAPoint ? 1 : 0)); // autoUseAAPoint (u8)

        stream.Write(0u);                                   // expandSlotInfos size=0 (sub_39B18A80, empty list)

        stream.Write((uint)character.JuryPoint);            // juryPoint
        stream.Write(0u);                                   // jailSeconds
        stream.Write(0u);                                   // reportedNo
        stream.Write(0u);                                   // suspectedNo
        stream.Write(0u);                                   // totalPlayTime

        stream.Write((byte)character.ExpandedExpert);       // expandedExpert (u8)
        stream.Write((byte)0);                              // remainBotCheckCnt (u8)
        stream.Write((short)0);                             // failedBotCheckAccumCnt (i16)

        for (var i = 0; i < 12; i++)
            stream.Write(0L);                               // instantTime[12]

        stream.Write(0u);                                   // dailyLeadershipPoint
        stream.Write(0L);                                   // lastDailyLeadershipPointTime
        stream.Write(0u);                                   // dailyHonorWarPoint
        stream.Write(0L);                                   // dailyHonorWarPointDate
        stream.Write(0u);                                   // totalReportBadUser
        stream.Write((byte)0);                              // usableAbilSetSlotCount (u8)

        stream.Write(0u);                                   // _pageInfos size=0 (UnitState_SerializePageInfoList)
        stream.Write(0u);                                   // _selectPageIndex
        stream.Write(0u);                                   // _extendMaxStats
        stream.Write(0u);                                   // _applyExtendCount

        stream.Write(0u);                                   // type
        stream.Write(0u);                                   // appellationStamp

        // equipSlotReinforces (optional group, always present): slotInfoList + levelEffectList, both empty
        stream.Write(0u);                                   // slotInfoList size=0 (sub_39390BA0)
        stream.Write(0u);                                   // levelEffectList size=0 (sub_393916B0)

        stream.Write(false);                                // reservedQuestDropTarget (bool)
        stream.Write(0u);                                   // merchantGoodsLimitPurchaseMap size=0 (sub_39A8ECB0)
        stream.Write(0u);                                   // actSanctionMap size=0 (sub_39A87330)
        stream.Write(0u);                                   // additionalSkillPoint

        return stream;
    }
}

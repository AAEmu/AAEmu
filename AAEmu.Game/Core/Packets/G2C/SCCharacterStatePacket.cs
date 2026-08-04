using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_CHARACTER_STATE (opcode 108) — sent on SelectCharacter to bridge the lobby into the world.
//   Character.WriteLobby1013) followed by the in-world "state" tail.
//   +120 i64, +128 u32, +144 u8, +152 i64, +160 u32, +168 i16, +248 bool, +464 = length-prefixed bytes.
public class SCCharacterStatePacket(Character character) : GamePacket(SCOffsets.SCCharacterStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)character.Transform.InstanceId); // iid
        // guid: serialized via ISerialize slot +464 (the same length-prefixed byte-string writer used for the
        // shows `10 00` (len=16) then a non-zero 16-byte guid. Derive a stable per-character guid from the id so
        // the client's identity keying is non-degenerate (the reference never sends an all-zero guid here).
        var guid = new byte[16];
        BitConverter.GetBytes((ulong)character.Id).CopyTo(guid, 0);
        BitConverter.GetBytes((ulong)character.Id ^ 0x5AA5_A55A_5AA5_A55AUL).CopyTo(guid, 8);
        stream.Write(guid, true);                           // guid (u16 length + 16 bytes)
        stream.Write(0u);                                   // rwd
        stream.Write(0u);                                   // srwd

        character.WriteLobby1013(stream);

        stream.Write(0f);                                   // angles.x
        stream.Write(0f);                                   // angles.y
        stream.Write(0f);                                   // angles.z (12-byte "angles" block, vtbl+184)

        stream.Write((uint)character.Experience);           // exp
        stream.Write(character.HeirExp);                    // heirExp (i64)
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

        stream.Write(0u);

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

        stream.Write((uint)character.UnitStateType);         // type
        stream.Write(character.AppellationStampId);          // appellationStamp

        // equipSlotReinforces (optional group, always present): slotInfoList + levelEffectList, both empty
        stream.Write(0u);
        stream.Write(0u);

        stream.Write(false);                                // reservedQuestDropTarget (bool)
        var merchantPurchases = NpcManager.Instance.GetMerchantPurchaseStates(character.Id);
        stream.Write((uint)merchantPurchases.Count);         // merchantGoodsLimitPurchaseMap size
        foreach (var (itemTemplateId, state) in merchantPurchases.OrderBy(entry => entry.Key))
        {
            stream.Write(itemTemplateId);                   // k: item template type (u32)
            stream.Write(state.BuyCount);                   // v.buyCount (i32)
            stream.Write((byte)state.PurchaseType);         // v.purchaseType (u8)
        }
        stream.Write(0u);
        stream.Write(0u);                                   // additionalSkillPoint

        return stream;
    }
}

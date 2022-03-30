using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterStatePacket : GamePacket
    {
        private readonly Character _character;

        public SCCharacterStatePacket(Character character) : base(SCOffsets.SCCharacterStatePacket, 5)
        {
            _character = character;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_character.InstanceId); // instanceId (iid)
            stream.Write(_character.Guid);       // guid
            stream.Write(0u);                    // rwd
            stream.Write(0u);                    // srwd

            #region Character

            #region Character_C1E0
            _character.Write(stream);
            #endregion
            
            //stream.Write(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xDB, 0xFB, 0x17, 0xC0 }); //angles
            stream.Write(_character.Transform.World.Position.X);        // X
            stream.Write(_character.Transform.World.Position.Y);        // Y
            stream.Write(_character.Transform.World.Position.Z);        // Z

            stream.Write(_character.Expirience);        // exp
            stream.Write(_character.HierExp);           // uint hierExp add in 3.5.0.3 NA, long in 5.7
            stream.Write(_character.RecoverableExp);    // recoverableExp
            stream.Write(0u);                           // penaltiedExp
            stream.Write(_character.ReturnDictrictId);  // returnDistrictId
            stream.Write(0u);                           // returnDistrict -> type(id)
            stream.Write(_character.ResurrectionDictrictId); // resurrectionDistrict -> type(id)

            for (var i = 0; i < 30; i++)                // 13 in 3.5, 30 in 5.7
            {
                stream.Write(0u);                       // abilityExp
            }

            stream.Write(0);                            // totalSentMail
            stream.Write(0);                            // totalMail
            stream.Write(0);                            // totalMiaMail
            stream.Write(0);                            // totalCommercialMail
            stream.Write(0);                            // unreadMail
            stream.Write(0);                            // unreadMiaMail
            stream.Write(0);                            // unreadCommercialMail
            stream.Write(_character.NumInventorySlots); // numInvenSlots
            stream.Write(_character.NumBankSlots);      // numBankSlots
            stream.Write(_character.Money);             // moneyAmount - Inventory
            stream.Write(_character.Money2);            // moneyAmount - Bank
            stream.Write(0L);                           // moneyAmount
            stream.Write(0L);                           // moneyAmount
            stream.Write(_character.AutoUseAAPoint);    // autoUseAAPoint (д.б. byte)

            // TODO Items in 5.7 проверить!
            var size = 0u;
            stream.Write(size); // size
            //for (var i = 0; i < size; i++)
            //{
            //    stream.Write((sbyte)0); // equipSlot

            //    foreach (var item in _character.Inventory.Equip)
            //    {
            //        if (item != null)
            //            stream.Write(item);
            //    }
            //}

            stream.Write(0);                // juryPoint
            stream.Write(0);                // jailSeconds

            stream.Write(0);                // reportedNo
            stream.Write(0);                // suspectedNo
            stream.Write(0);                // totalPlayTime

            stream.Write(_character.ExpandedExpert); // expandedExpert
            stream.Write(0);                // remainBotCheckCnt
            stream.Write((short)0);         // failedBotCheckAccumCnt

            for (var i = 0; i < 12; i++)     // 8 in 3.5, 9 in 5.7, 12 in 7+
            {
                stream.Write(0L);            // instantTime
            }
            stream.Write(0u);                // dailyLeadershipPoint
            stream.Write(DateTime.MinValue); // lastDailyLeadershipPointTime
            stream.Write(0u);                // dailyHonorWarPoint
            stream.Write(DateTime.MinValue); // dailyHonorWarPointDate
            stream.Write(0);                 // totalReportBadUser
            stream.Write((byte)0);           // usableAbilSetSlotCount

            #region read_Stats_0
            for (var i = 0; i < 5; i++)
            {
                    stream.Write(0u);   // stats
            }
            #endregion

            #region _pageInfos 
            size = 1u; // TODO _pageInfos
            stream.Write(size); // size
            for (var i = 0; i < size; i++)
            {
                for (var j = 0; j < 5; j++)
                {
                    stream.Write(0); // stats
                }
                stream.Write(0u); // applyNormalCount
                stream.Write(0u); // applySpecialCount
            }
            #endregion

            stream.Write(0u);       // _selectPageIndex
            stream.Write(0u);       // _extendMaxStats
            stream.Write(0u);       // _applyExtendCount
            stream.Write(0u);       // type
            stream.Write(0);        // appellationStamp

            size = 0u;
            // TODO equipSlotReinforces
            stream.Write(size); // size
            // TODO slotInfoList
            stream.Write(size); // size
            for (var i = 0; i < size; i++)
            {
                stream.Write(0);       // k
                stream.Write((byte)0); // level
                stream.Write(0);       // exp
            }
            // TODO levelEffectList
            size = 0u;
            stream.Write(size); // size
            for (var i = 0; i < size; i++)
            {
                stream.Write((byte)0);  // equipSlot
                stream.Write((sbyte)0); // level
                stream.Write(0u);       // type
            }
            stream.Write((byte)0);     // reservedQuestDropTarget
            #endregion

            return stream;
        }
    }
}

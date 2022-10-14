using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartInteractionPacket : GamePacket
    {
        public CSStartInteractionPacket() : base(CSOffsets.CSStartInteractionPacket, 1)
        {
        }
        
        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var objId = stream.ReadBc();
            var extraInfo = stream.ReadInt32();
            var pickId = stream.ReadInt32();
            var mouseButton = stream.ReadByte();
            var modifierKeys = stream.ReadInt32();

            _log.Warn("StartInteraction, NpcObjId: {0}, objId: {1}, extraInfo: {2}, pickId: {3}, mouse: {4}, mods: {5}",
                npcObjId, objId, extraInfo, pickId, mouseButton, modifierKeys);

            var npc = WorldManager.Instance.GetNpc(npcObjId);
            // TODO: Distance-check
            if (npc != null)
            {
                // The returned skillsList is supposed to be a list of what actions you can take, and the client will
                // use the first one regardless of what you put in there.
                // Also noted is that even when you send a zero (0) skill list back (one skill of 0),
                // it will still use the first action that is prompted to the user. This effectively makes quest NPCS
                // right-clickable as intended
                // This could later be used to implement some of the anti-cheating
                // 0 is the intended default or else quests go wonky
                
                uint option = 0;
                if (npc.Template.Banker)
                    option = SkillsEnum.UseWarehouse; // Open warehouse
                // TODO: fill in the skills and maybe change the order to what it would show in-game
                else if (npc.Template.AbilityChanger)
                    option = SkillsEnum.ChangeSkillsets; // Open Skill-Trainer
                else if (npc.Template.Auctioneer)
                    option = SkillsEnum.UseAuctioneer; // Open Auctioneer
                else if (npc.Template.Priest)
                    option = SkillsEnum.Blessing; // Open Recover-Exp dialog ?
                else if (npc.Template.Repairman)
                    option = SkillsEnum.Repair; // Open Repair dialog ?
                else if (npc.Template.Merchant)
                    option = SkillsEnum.UseStore; // Open Shop dialog ?
                else if (npc.Template.Stabler)
                    option = SkillsEnum.HealPetSWounds; // Open Pet Recovery dialog ?
                else if (npc.Template.Expedition)
                    option = SkillsEnum.FormGuild; // Open Repair dialog ?
                else if (npc.Template.RecrutingBattlefieldId > 0)
                    option = SkillsEnum.WarSupport; // Open Arena dialog ?
                else if (npc.Template.Blacksmith)
                    option = SkillsEnum.ItemFusion; // Open Item Fuse dialog ?

                Connection.ActiveChar.SendPacket(new SCNpcInteractionSkillListPacket(npcObjId, objId, extraInfo,
                    pickId, mouseButton, modifierKeys, new uint[] { option }));
            }

            var slave = WorldManager.Instance.GetUnit(npcObjId);
            if (slave is Mate mate)
            {
                Connection.ActiveChar.SendPacket(new SCNpcInteractionSkillListPacket(npcObjId, objId, extraInfo, pickId, mouseButton, modifierKeys, new uint[] { SkillsEnum.SlaveMounting }));
            }
        }
    }
}

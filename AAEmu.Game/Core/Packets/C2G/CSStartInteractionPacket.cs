using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Skills;

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
                
                var skills = new List<uint>();
                if (npc.Template.Banker)
                    skills.Add(SkillsEnum.UseWarehouse); // Open warehouse
                // TODO: fill in the skills and maybe change the order to what it would show in-game
                else if (npc.Template.AbilityChanger)
                    skills.Add(SkillsEnum.ChangeSkillsets); // Open Skill-Trainer
                else if (npc.Template.Auctioneer)
                    skills.Add(SkillsEnum.UseAuctioneer); // Open Auctioneer
                else if (npc.Template.Priest)
                    skills.Add(SkillsEnum.Blessing); // Open Recover-Exp dialog ?
                else if (npc.Template.Repairman)
                    skills.Add(SkillsEnum.Repair); // Open Repair dialog ?
                else if (npc.Template.Merchant)
                    skills.Add(SkillsEnum.UseStore); // Open Shop dialog ?
                else if (npc.Template.Stabler)
                    skills.Add(SkillsEnum.HealPetSWounds); // Open Pet Recovery dialog ?
                else if (npc.Template.Expedition)
                    skills.Add(SkillsEnum.FormGuild); // Open Repair dialog ?
                else if (npc.Template.RecrutingBattlefieldId > 0)
                    skills.Add(SkillsEnum.WarSupport); // Open Arena dialog ?
                else if (npc.Template.Blacksmith)
                    skills.Add(SkillsEnum.ItemFusion); // Open Item Fuse dialog ?

                // Add a dummy-skill of 0 when we didn't intercept anything.
                // This also fixes the right-click on quest NPCs
                if (skills.Count <= 0)
                    skills.Add(0);
                
                if (skills.Count > 0)
                    Connection.ActiveChar.SendPacket(new SCNpcInteractionSkillListPacket(npcObjId, objId, extraInfo,
                        pickId, mouseButton, modifierKeys, skills.ToArray()));
            }
        }
    }
}

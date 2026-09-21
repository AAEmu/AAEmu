using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartInteractionPacket() : GamePacket(CSOffsets.CSStartInteractionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var npcObjId = stream.ReadBc();
        var objId = stream.ReadBc();
        var extraInfo = stream.ReadInt32();
        var pickId = stream.ReadInt32();
        var mouseButton = stream.ReadByte();
        var modifierKeys = stream.ReadInt32();

        Logger.Warn("StartInteraction, NpcObjId: {0}, objId: {1}, extraInfo: {2}, pickId: {3}, mouse: {4}, mods: {5}",
            npcObjId, objId, extraInfo, pickId, mouseButton, modifierKeys);

        var character = Connection.ActiveChar;
        var npc = character?.ParentWorld?.GetNpc(npcObjId);
        if (npc != null)
        {
            character.CurrentInteractionObject = npc;

            // The client renders one dynamic action per entry, so the reply carries everything the NPC
            // offers: the gated store front, the service skill its template flags name, and the skills
            // its authored interaction set adds. An empty list is a valid answer; a zero entry is not,
            // because the client resolves an icon per entry and reports skill type 0 as a missing asset.
            //
            // pickId is not a menu index and is not validated here: it is the client's pick handle for
            // pick-based interactions and arrives as -1 on the plain interact/reply path, while choosing
            // an entry on the interaction bar makes the client cast that skill itself. What the NPC may
            // offer is therefore decided when the list is built - the gated stores below and the authored
            // set - and the skill a client then casts is checked by the skill's own requirements.
            var skills = NpcInteractionRules
                .ComposeSkills(
                    npc.Template,
                    QuestManager.Instance.IsQuestTalkNpc(npc.TemplateId),
                    NpcInteractionGameData.Instance.GetSkills(npc.Template.NpcInteractionSetId))
                .ToList();

            var storeSkill = 0u;
            if (npc.Template.TradeGoodBuy)
            {
                if (SpecialtyManager.Instance.CanStartTradeGoodInteraction(character, npc))
                    storeSkill = SkillsEnum.UseTradeGoodStore;
            }
            else if (npc.Template.Specialty)
            {
                if (SpecialtyManager.Instance.CanStartSpecialtyInteraction(character, npc))
                    storeSkill = SkillsEnum.UseSpecialtyStore;
            }

            if (storeSkill > 0 && !skills.Contains(storeSkill))
                skills.Insert(0, storeSkill);

            character.SendPacket(new SCNpcInteractionSkillListPacket(npcObjId, objId, extraInfo,
                pickId, mouseButton, modifierKeys, [.. skills]));
        }

        var slave = character?.ParentWorld?.GetUnit(npcObjId);
        if (slave is Mate mate)
        {
            character.SendPacket(new SCNpcInteractionSkillListPacket(npcObjId, objId, extraInfo, pickId, mouseButton, modifierKeys, [SkillsEnum.SlaveMounting]));
        }
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// <c>bool isTargetChanged</c>.
/// </remarks>
public class CSInteractNPCPacket() : GamePacket(CSOffsets.CSInteractNPCPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var isTargetChanged = stream.ReadBoolean();

        Logger.Debug("InteractNPC, BcId: {0}, TargetChanged: {1}", objId, isTargetChanged);

        var character = Connection.ActiveChar;
        if (character == null || objId == 0 || character.ParentWorld.GetUnit(objId) is not Npc npc)
        {
            Logger.Warn(
                "Rejected NPC interaction target {0} from {1} ({2})",
                objId, character?.Name ?? "<disconnected>", character?.ObjId ?? 0);
            return;
        }

        character.CurrentInteractionObject = npc;

        if (isTargetChanged)
            character.CurrentTarget = npc;

        // A zero-entry table is the native representation of no known aggro for this NPC.
        Connection.SendPacket(new SCAiAggroPacket(objId));

        // F-talk only sent this packet. Without the skill list the client opens
        // a directing window that cannot confirm (no start packet). Same body
        // as right-click CSStartInteraction, extraInfo=1 / empty pick.
        // Every offered skill becomes a button on the client's dynamic action bar, so the NPC's
        // service skill and the actions of its authored interaction set are listed together.
        var skills = NpcInteractionRules.ComposeSkills(
            npc.Template,
            QuestManager.Instance.IsQuestTalkNpc(npc.TemplateId),
            NpcInteractionGameData.Instance.GetSkills(npc.Template.NpcInteractionSetId));
        character.SendPacket(new SCNpcInteractionSkillListPacket(objId, 0, 1, 0, 0, 0, [.. skills]));

        // The cargo dialog reads the native goods cache without requesting its initial page.
        if (npc.Template.TradeGoodBuy)
            SpecialtyManager.Instance.SendBuyList(character, npc.ObjId);

        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayInteractNpcToZone?.Invoke(character.ObjId, objId, false);
    }
}

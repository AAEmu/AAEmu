using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLearnBuffPacket() : GamePacket(CSOffsets.CSLearnBuffPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var buffId = stream.ReadUInt32();

        var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
        // Auto mode: tree / active rows are server-granted. A leftover click only syncs or refreshes.
        if (PassiveTreeAutoGrantRules.TreePassivesAutoGrant &&
            template != null &&
            PassiveTreeAutoGrantRules.IsAutoGrantCandidate(template))
        {
            var character = Connection.ActiveChar;
            if (character.Skills.PassiveBuffs.ContainsKey(buffId))
                character.BroadcastPacket(new SCBuffLearnedPacket(character.ObjId, buffId), true);
            else
                character.Skills.RefreshAutoGrantedPassives(notify: true);
            return;
        }

        // Click mode (or paid generals): learn this one id. Tree rows gate on skills unlocked in
        // that tree and cost 0 skill points, so a spent pool does not block them.
        Connection.ActiveChar.Skills.AddBuff(buffId);
    }
}

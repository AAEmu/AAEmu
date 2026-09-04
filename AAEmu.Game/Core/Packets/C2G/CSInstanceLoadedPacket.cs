using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInstanceLoadedPacket() : GamePacket(CSOffsets.CSInstanceLoadedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
        // TODO Debug

        var me = Connection.ActiveChar;
        if (me == null)
            return;

        Connection.SendPacket(new SCUnitStatePacket(me));
        Connection.SendPacket(new SCListSkillActiveTypePacket(me.SkillActiveTypes.BuildPacketEntries()));
        Connection.SendPacket(new SCHeirSkillListPacket(me.HeirSkills.BuildPacketEntries()));
        if (me.Faction != null && (uint)me.Faction.Id != 0)
            Connection.SendPacket(new SCUnitFactionChangedPacket(
                me.ObjId, me.Name ?? "", FactionsEnum.Invalid, me.Faction.Id, false));
        Connection.SendPacket(new SCCooldownsPacket(me.Cooldowns));
        Connection.SendPacket(TimeOfDayClientPackets.Hour(TimeManager.Instance.GetTime));

        me.DisabledSetPosition = false;

        Logger.Debug("InstanceLoaded.");
    }
}

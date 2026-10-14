using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.InstantGame;
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
        // The shared world clock belongs to the open world. An instance ships its own static time-of-day and
        // owns its own clock (the zone reports that one), so pushing the open-world hour here fought both and
        // left the instance lit wrong. Main-world loads still take the shared hour.
        if (TimeManager.ZoneUsesSharedGameDay(me.Transform.ZoneId))
            Connection.SendPacket(TimeOfDayClientPackets.Hour(TimeManager.Instance.GetTime));

        // Stream the neighbourhood now so a cold load paints the room under the loading screen.
        // A dungeon copy keeps movement locked: unlocking here lets a client that already cached
        // the level start falling through a floor that has no collision yet. That unlock waits
        // for the re-entry check that follows the closed loading screen. Leave-to-overworld
        // and non-copy loads still finish here.
        if (InstantGameHandoverRules.CompletesArrivalOnInstanceLoaded(me.ParentWorld?.DungeonInstance != null))
            InstanceArrival.Complete(me, snapClient: false);
        else
            InstanceArrival.StreamNeighbourhood(me);

        // The mentoring accept sources removed in 4.0 are restored only after the client confirms a
        // successful dungeon load. Re-entry and reconnect are harmless: active and same-day completed
        // quests are rejected by the restoration gate.
        me.Quests.TryStartRestoredMentoringQuestOnDungeonEntry();

        // A dungeon copy is handed over (which copy, its rounds and its HUD readouts) once the loading
        // screen has closed, in answer to the client's re-entry check: the client drops the UI events it
        // raises while the loading screen is up, so a hand-over sent from here never reaches its UI.
        if (me.ParentWorld?.DungeonInstance != null)
            me.MarkInstantGameHandoverPending();

        // The load just confirmed is the battle field copy this character was invited into: this
        // is the join event that seats them in the match (idempotent inside the match).
        me.CurrentInstantGame?.OnEnterWorld(me, 0ul);

        Logger.Debug("InstanceLoaded.");
    }
}

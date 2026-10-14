using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;

namespace AAEmu.Game.Core.Packets.C2G;

// C2S re-entry check (opcode 0x12E), sent fire-and-forget by the client whenever it leaves a loading screen or
// enters the world. Outside a dungeon copy it gets no answer. After a load into a copy it is the first moment
// the client's UI takes events again, so the copy's hand-over that the instance load left owed is sent here,
// and movement that stayed locked during the load is unlocked (with a snap back onto the spawn).
public class CSReentryCheckPacket() : GamePacket(CSOffsets.CSReentryCheckPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var me = Connection?.ActiveChar;
        if (me == null)
            return;

        var dungeon = me.ParentWorld?.DungeonInstance;
        var insideCopy = dungeon != null;
        if (InstantGameHandoverRules.CompletesArrivalOnReentryCheck(me.DisabledSetPosition, insideCopy))
            InstanceArrival.Complete(me, snapClient: true);

        if (!InstantGameHandoverRules.ShouldHandOverOnReentryCheck(me.TakeInstantGameHandoverPending(), insideCopy))
            return;

        Logger.Debug($"Reentry check: handing over dungeon copy to {me.Name}");
        dungeon.SendDungeonEntryHandshake(me);
    }
}

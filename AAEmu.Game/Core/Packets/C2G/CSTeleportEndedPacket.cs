using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTeleportEndedPacket() : GamePacket(CSOffsets.CSTeleportEndedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var ori = stream.ReadBytes(16); // TODO example: 00000000 00000000 00000000 0000803F

        var me = Connection.ActiveChar;
        if (me == null)
            return;

        // Same-instance GM /move and SCTeleportUnit never send CSInstanceLoaded.
        // Clearing the movement lock without applying the arrival left World
        // Transform on the old cell, so later GM spawns used dry-land coords
        // while the client was already at the destination.
        me.DisabledSetPosition = false;
        var rot = me.Transform.World.Rotation;
        me.SetPosition(x, y, z, rot.X, rot.Y, rot.Z);
        me.Transform.FinalizeTransform();
        Logger.Info("TeleportEnded applied {0} -> ({1:0.0},{2:0.0},{3:0.0}) zone={4}",
            me.Name, x, y, z, me.Transform.ZoneId);

        WorldManager.ResendVisibleObjectsToCharacter(me);
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBindSlavePacket() : GamePacket(CSOffsets.CSBindSlavePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // CN 10.0.2.13: tl (s16) + skillType (s32). skillType is the occupy skill id
        // (e.g. 12076 점유), NOT AttachPointKind — mast/sail climb sends this after hang.
        var tlId = stream.ReadUInt16();
        var skillType = stream.ReadInt32();

        Logger.Debug("BindSlave, Tl: {0}, SkillType: {1}", tlId, skillType);
        var character = Connection.ActiveChar;
        if (character?.ParentWorld == null)
            return;

        var slave = character.ParentWorld.SlaveManager.FindSlaveByTlId(tlId);
        if (slave == null || slave.IsDead)
            return;

        // Leave mast/ladder hang first — sticky + BindSlave = client skill_source_is_hanging.
        // Always SCUnhung(self): client may still be hung after CSUnhang (was broadcast without self).
        var sticky = character.Transform.StickyParent;
        var hangTarget = sticky?.GameObject?.ObjId ?? 0;
        if (sticky != null)
        {
            var stickySlave = sticky.GameObject as Slave;
            character.Transform.StickyParent = null;
            if (stickySlave != null)
                ShipHarpoonRopeController.BreakRopeForClients(stickySlave, cutouted: false);
        }

        character.BroadcastPacket(new SCUnhungPacket(character.ObjId, hangTarget, 0), true);

        // Client Tl picks hull (helm) or equipment sail (SlaveKind=8). Seat is always Driver
        // for CSBindSlave; doodad attachments pass AttachPointKind via BindSlave overload.
        character.ParentWorld.SlaveManager.BindSlave(
            character, slave.ObjId, AttachPointKind.Driver, AttachUnitReason.NewMaster);
    }
}

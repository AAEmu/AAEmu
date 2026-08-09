using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.DoodadObj;

public class BondDoodad : PacketMarshaler
{
    private Doodad _owner;
    private readonly byte _attachPoint;
    private readonly BondKind _kind;
    private readonly int _space;
    private readonly int _spot;

    public uint ObjId => _owner?.ObjId ?? 0;
    public AttachPointKind AttachPoint => (AttachPointKind)_attachPoint;
    public BondKind Kind => _kind;
    public int Space => _space;
    public int Spot => _spot;

    public BondDoodad(AttachPointKind attachPoint, BondKind kind, int space, int spot)
    {
        _attachPoint = (byte)attachPoint;
        _kind = kind;
        _space = space;
        _spot = spot;
    }

    public BondDoodad(Doodad owner, AttachPointKind attachPoint, BondKind kind, int space, int spot)
    {
        SetOwner(owner);
        _attachPoint = (byte)attachPoint;
        _kind = kind;
        _space = space;
        _spot = spot;
    }

    public void SetOwner(Doodad owner)
    {
        _owner = owner;
    }

    public Doodad GetOwner()
    {
        return _owner;
    }

    /// <summary>
    /// Whether a CS move should stand the unit up from a bond.
    /// Seat-settle packets often still report residual velocity; only Moving / Jumping mean leave.
    /// CSUnbond is always leave and does not use this.
    /// </summary>
    public static bool IsIntentionalSeatLeave(MoveTypeFlags flags, ushort actorFlags)
    {
        if (((MoveTypeActorFlags)actorFlags).HasFlag(MoveTypeActorFlags.Jumping))
            return true;
        // Moving (0x02) and Jumping combo (Moving|Stopping).
        return flags.HasFlag(MoveTypeFlags.Moving);
    }

    /// <summary>
    /// Clears seat occupancy, transform parenting, SCUnbond, zone unbond, and remove_on_unbond buffs.
    /// No-op when not bonded. When <paramref name="expectedDoodadObjId"/> is set, requires a match.
    /// </summary>
    public static bool TryRelease(Character character, uint? expectedDoodadObjId = null)
    {
        if (character?.Bonding == null)
            return false;

        var bonding = character.Bonding;
        var doodadObjId = bonding.ObjId;
        if (expectedDoodadObjId is { } expect && doodadObjId != expect)
            return false;

        var doodad = bonding.GetOwner();
        if (doodad != null)
            doodad.Seat.UnLoadPassenger(character, doodad.ObjId);

        bonding.SetOwner(null);
        character.Bonding = null;
        character.Transform.Parent = null;
        character.Transform.StickyParent = null;

        character.BroadcastPacket(new SCUnbondDoodadPacket(character.ObjId, character.Id, doodadObjId), true);
        WorldIntegration.RelayBondDoodadToZone?.Invoke(character.ObjId, bonding, false);
        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unbond);
        return true;
    }

    /// <summary>
    /// Trailing root on WZUnitBondToDoodad must be a zone unit ObjId (or 0), never the seat doodad.
    /// Free-world furniture has no house/slave parent → root 0.
    /// </summary>
    public static uint ResolveZoneRootUnitId(Doodad seat)
    {
        if (seat == null)
            return 0;

        for (GameObject cur = seat.ParentObj; cur != null; cur = cur.ParentObj)
        {
            if (ObjectIdManager.IsZoneUnitId(cur.ObjId))
                return cur.ObjId;
        }

        if (ObjectIdManager.IsZoneUnitId(seat.ParentObjId))
            return seat.ParentObjId;

        for (var t = seat.Transform?.StickyParent; t != null; t = t.StickyParent)
        {
            var go = t.GameObject;
            if (go != null && ObjectIdManager.IsZoneUnitId(go.ObjId))
                return go.ObjId;
        }

        return 0;
    }

    /// <summary>
    /// point u8, doodad bc(3), space s32, spot s32, type u32 (BondKind).
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_attachPoint);
        stream.WriteBc(_owner?.ObjId ?? 0);
        stream.Write(_space);
        stream.Write(_spot);
        stream.Write((uint)_kind);
        return stream;
    }
}

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;

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
    public uint ParentObjId { get; }
    public bool IsMovingParent { get; }

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

        ParentObjId = owner?.ParentObjId ?? 0;
        if (ParentObjId != 0 && owner?.ParentWorld != null)
        {
            var parent = owner.ParentWorld.GetBaseUnit(ParentObjId);
            IsMovingParent = parent is Slave or Transfer;
        }
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
    /// point u8, doodad bc(3), space s32, spot s32, type u32 (BondKind).
    /// Older AAEmu wrote kind before space/spot as u8 — client never bonded → no sit.
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

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZCreateDoodad (0x073) — World → Zone doodad Create.
/// bc objId, pish+pisc{designId, modelId, backpackItemId, field30}, flag, bc field8, bc parentId,
/// attachPoint u8, worldPos 11B, rot s16×3, scale, type1/2 s64, type3/growing u32, plantTime u64,
/// family/puzzle s32, ownerType u8, dbHouseId, data/data2, updatedTime,
/// [freshness if ItemBackpack(backpackItemId).type ∈ {3=goods,8=tradegoods}], type6/7 s64.
/// </summary>
public class WZCreateDoodadPacket : ZonePacket
{
    private readonly Doodad _doodad;
    private readonly IItemManager? _itemManager;

    public WZCreateDoodadPacket(Doodad doodad)
        : base(WzOpcodes.CreateDoodad)
    {
        _doodad = doodad;
    }

    public WZCreateDoodadPacket(Doodad doodad, IItemManager itemManager)
        : base(WzOpcodes.CreateDoodad)
    {
        _doodad = doodad;
        _itemManager = itemManager;
    }

    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(_doodad.ObjId);

        // map keyed by item_id). Optional freshness block only when desc+8 (backpack_type_id)
        // is goods(3) or tradegoods(8). Static world doodads keep pisc[2]=0 (same as SC).
        var itemManager = _itemManager ??
                          (_doodad.ItemId == 0 && _doodad.ItemTemplateId == 0 ? null : ItemManager.Instance);
        var goods = default(DoodadPhysicalGoods);
        var isGoods = itemManager != null && DoodadPhysicalGoods.TryResolve(_doodad, itemManager, out goods);
        // designId / modelId / backpackItemId / field30 — MUST be pish/pisc
        // modelId=0 → Zone uses doodad_almighties.model (needs hook_zone_doodad_db_model).
        // Non-zero modelId uses models.name, which LoadCGF often rejects → pumpkin default.
        stream.WritePisc(_doodad.TemplateId, 0u, isGoods ? goods.ItemTemplateId : 0u, 0u);
        stream.Write((byte)0); // flag bits
        stream.WriteBc(_doodad.OwnerObjId);
        stream.WriteBc(_doodad.ParentObjId);
        stream.Write((byte)_doodad.AttachPoint);

        var useLocal = _doodad.AttachPoint > 0 || _doodad.ParentObjId > 0;
        var pos = useLocal
            ? _doodad.Transform.Local.Position
            : ZoneCoordBoundary.ToZoneLocal(_doodad.Transform.ZoneId, _doodad.Transform.World.Position);
        var (roll, pitch, yaw) = useLocal
            ? _doodad.Transform.Local.ToRollPitchYawShorts()
            : _doodad.Transform.World.ToRollPitchYawShorts();
        stream.Write(Helpers.ConvertPosition(pos.X, pos.Y, pos.Z), false);
        stream.Write(roll);
        stream.Write(pitch);
        stream.Write(yaw);
        stream.Write(_doodad.Scale);

        stream.Write((long)_doodad.OwnerId); // type1
        stream.Write((long)_doodad.ItemTemplateId); // type2
        stream.Write(_doodad.FuncGroupId); // type3 / phase group
        stream.Write(_doodad.TimeLeft); // growing
        var plantTime = _doodad.OwnerType == DoodadOwnerType.System
                        || _doodad.PlantTime == default
                        || _doodad.PlantTime <= DateTime.UnixEpoch
            ? 0UL
            : (ulong)new DateTimeOffset(_doodad.PlantTime.ToUniversalTime()).ToUnixTimeSeconds();
        stream.Write(plantTime);
        stream.Write(0); // family
        stream.Write(_doodad.PuzzleGroup);
        stream.Write((byte)_doodad.OwnerType);
        stream.Write(_doodad.OwnerDbId);
        stream.Write(_doodad.Data);
        stream.Write(0); // data2
        stream.Write((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // updatedTime
        if (isGoods)
        {
            stream.Write(goods.FreshnessTime);
            stream.Write(0L); // unnamed physical goods field
            stream.Write((ushort)0); // unnamed physical goods field
        }
        stream.Write(0L); // type6
        stream.Write(0L); // type7
    }
}

public class WZRemoveDoodadPacket(uint objId) : ZonePacket(WzOpcodes.RemoveDoodad)
{
    protected override void WriteBody(PacketStream stream) => stream.WriteBc(objId);
}

public class WZDoodadChangePhasePacket(uint objId, uint funcGroupId) : ZonePacket(WzOpcodes.DoodadChangePhase)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(funcGroupId);
    }
}

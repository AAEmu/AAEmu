using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZCreateDoodad (0x073) — World → Zone doodad Create.
/// bc objId, pish+pisc{designId, modelId, backpackItemId, field30}, flag, bc field8, bc parentId,
/// attachPoint u8, worldPos 11B, rot s16×3, scale, type1/2 s64, type3/growing u32, plantTime u64,
/// family/puzzle s32, ownerType u8, dbHouseId, data/data2, updatedTime,
/// [freshness if ItemBackpack(backpackItemId).type ∈ {3=goods,8=tradegoods}], type6/7 s64.
/// </summary>
public class WZCreateDoodadPacket(Doodad doodad) : ZonePacket(WzOpcodes.CreateDoodad)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(doodad.ObjId);

        // map keyed by item_id). Optional freshness block only when desc+8 (backpack_type_id)
        // is goods(3) or tradegoods(8). Static world doodads keep pisc[2]=0 (same as SC).
        var (backpackItemId, needsFreshness) = ResolveBackpackFreshness(doodad);
        // designId / modelId / backpackItemId / field30 — MUST be pish/pisc
        // modelId=0 → Zone uses doodad_almighties.model (needs hook_zone_doodad_db_model).
        // Non-zero modelId uses models.name, which LoadCGF often rejects → pumpkin default.
        stream.WritePisc(doodad.TemplateId, 0u, backpackItemId, 0u);
        stream.Write((byte)0); // flag bits
        stream.WriteBc(doodad.OwnerObjId);
        stream.WriteBc(doodad.ParentObjId);
        stream.Write((byte)doodad.AttachPoint);

        var useLocal = doodad.AttachPoint > 0 || doodad.ParentObjId > 0;
        var pos = useLocal
            ? doodad.Transform.Local.Position
            : ZoneCoordBoundary.ToZoneLocal(doodad.Transform.ZoneId, doodad.Transform.World.Position);
        var (roll, pitch, yaw) = useLocal
            ? doodad.Transform.Local.ToRollPitchYawShorts()
            : doodad.Transform.World.ToRollPitchYawShorts();
        stream.Write(Helpers.ConvertPosition(pos.X, pos.Y, pos.Z), false);
        stream.Write(roll);
        stream.Write(pitch);
        stream.Write(yaw);
        stream.Write(doodad.Scale);

        stream.Write((long)doodad.OwnerId); // type1
        stream.Write((long)doodad.ItemTemplateId); // type2
        stream.Write(doodad.FuncGroupId); // type3 / phase group
        stream.Write(doodad.TimeLeft); // growing
        var plantTime = doodad.OwnerType == DoodadOwnerType.System
                        || doodad.PlantTime == default
                        || doodad.PlantTime <= DateTime.UnixEpoch
            ? 0UL
            : (ulong)new DateTimeOffset(doodad.PlantTime.ToUniversalTime()).ToUnixTimeSeconds();
        stream.Write(plantTime);
        stream.Write(0); // family
        stream.Write(doodad.PuzzleGroup);
        stream.Write((byte)doodad.OwnerType);
        stream.Write(doodad.OwnerDbId);
        stream.Write(doodad.Data);
        stream.Write(0); // data2
        stream.Write((ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()); // updatedTime
        if (needsFreshness)
        {
            stream.Write(0UL); // freshnessTime
            stream.Write(0L); // type4
            stream.Write((ushort)0); // type5
        }
        stream.Write(0L); // type6
        stream.Write(0L); // type7
    }

    /// <summary>
    /// Retail: pisc[2] = item_backpacks.item_id when this doodad is a put-down pack; else 0.
    /// Freshness fields follow only for backpack_type goods(3) / tradegoods(8).
    /// </summary>
    private static (uint backpackItemId, bool needsFreshness) ResolveBackpackFreshness(Doodad doodad)
    {
        if (doodad.ItemTemplateId == 0)
            return (0u, false);

        if (ItemManager.Instance.GetTemplate(doodad.ItemTemplateId) is not BackpackTemplate backpack)
            return (0u, false);

        var needsFreshness = backpack.BackpackType is BackpackType.TradePack or BackpackType.TradeGoods;
        return (doodad.ItemTemplateId, needsFreshness);
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

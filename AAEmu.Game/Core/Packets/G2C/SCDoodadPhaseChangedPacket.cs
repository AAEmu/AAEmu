using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadPhaseChangedPacket : GamePacket
{
    private readonly Doodad _doodad;
    private readonly uint _funcGroupId;
    private readonly IItemManager _itemManager;

    public SCDoodadPhaseChangedPacket(Doodad doodad) : this(doodad, doodad.FuncGroupId)
    {
    }

    public SCDoodadPhaseChangedPacket(Doodad doodad, uint funcGroupId) : base(SCOffsets.SCDoodadPhaseChangedPacket, 1)
    {
        _doodad = doodad;
        _funcGroupId = funcGroupId;
        Logger.Trace("[Doodad] [0] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _funcGroupId, _doodad.TimeLeft);
    }

    public SCDoodadPhaseChangedPacket(Doodad doodad, IItemManager itemManager)
        : base(SCOffsets.SCDoodadPhaseChangedPacket, 1)
    {
        _doodad = doodad;
        _funcGroupId = doodad.FuncGroupId;
        _itemManager = itemManager;
        Logger.Trace("[Doodad] [0] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _funcGroupId, _doodad.TimeLeft);
    }

    public override PacketStream Write(PacketStream stream)
    {
        Logger.Debug("[Doodad] [2] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _funcGroupId, _doodad.TimeLeft);

        // bc objId, u32 newFuncGroupId, u32 data, u32 growing, i32 puzzleGroup, u32 itemTemplateId,
        // bool isGoods (+ optional freshness/type/type when true).
        // Old layout omitted `data` + `isGoods` → client over-read into the next SC packet and/or
        // never applied the open/close model swap (housing door tpl 4566 stayed closed).
        stream.WriteBc(_doodad.ObjId);
        stream.Write(_funcGroupId);
        stream.Write(_doodad.Data);
        stream.Write(_doodad.TimeLeft); // growing
        stream.Write(_doodad.PuzzleGroup);
        var itemManager = _itemManager ??
                          (_doodad.ItemId == 0 && _doodad.ItemTemplateId == 0 ? null : ItemManager.Instance);
        var goods = default(DoodadPhysicalGoods);
        var isGoods = itemManager != null && DoodadPhysicalGoods.TryResolve(_doodad, itemManager, out goods);
        stream.Write(isGoods ? goods.ItemTemplateId : _doodad.ItemTemplateId);
        stream.Write(isGoods);
        if (isGoods)
        {
            stream.Write(goods.FreshnessTime);
            stream.Write(0L); // unnamed physical goods field
            stream.Write((ushort)0); // unnamed physical goods field
        }
        return stream;
    }
}

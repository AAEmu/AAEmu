using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadPhaseChangedPacket : GamePacket
{
    private readonly Doodad _doodad;

    public SCDoodadPhaseChangedPacket(Doodad doodad) : base(SCOffsets.SCDoodadPhaseChangedPacket, 1)
    {
        _doodad = doodad;
        Logger.Trace("[Doodad] [0] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _doodad.FuncGroupId, _doodad.TimeLeft);
    }

    public override PacketStream Write(PacketStream stream)
    {
        Logger.Debug("[Doodad] [2] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _doodad.FuncGroupId, _doodad.TimeLeft);

        // bc objId, u32 newFuncGroupId, u32 data, u32 growing, i32 puzzleGroup, u32 itemTemplateId,
        // bool isGoods (+ optional freshness/type/type when true).
        // Old layout omitted `data` + `isGoods` → client over-read into the next SC packet and/or
        // never applied the open/close model swap (housing door tpl 4566 stayed closed).
        stream.WriteBc(_doodad.ObjId);
        stream.Write(_doodad.FuncGroupId);
        stream.Write(_doodad.Data);
        stream.Write(_doodad.TimeLeft); // growing
        stream.Write(_doodad.PuzzleGroup);
        stream.Write(_doodad.ItemTemplateId);
        stream.Write(false); // isGoods — coffer/goods path not used for doors
        return stream;
    }
}

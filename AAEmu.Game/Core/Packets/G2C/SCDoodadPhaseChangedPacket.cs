using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadPhaseChangedPacket : GamePacket
{
    private Doodad _doodad;

    public SCDoodadPhaseChangedPacket(Doodad doodad) : base(SCOffsets.SCDoodadPhaseChangedPacket, 5)
    {
        _doodad = doodad;
        Logger.Trace("[Doodad] [0] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _doodad.FuncGroupId, _doodad.TimeLeft);
    }

    public override PacketStream Write(PacketStream stream)
    {
        Logger.Debug("[Doodad] [2] SCDoodadPhaseChangedPacket: TemplateId {0}, ObjId {1},  CurrentPhaseId {2}, TimeLeft {3}", _doodad.TemplateId, _doodad.ObjId, _doodad.FuncGroupId, _doodad.TimeLeft);
        stream.WriteBc(_doodad.ObjId);
        stream.Write(_doodad.FuncGroupId);
        stream.Write(_doodad.TimeLeft); // growing
        stream.Write(-1); // puzzleGroup
        stream.Write(_doodad.ItemTemplateId); // type(id) for backpack e.g. Id=27606 Sturgeon Pack

        var isGoods = false;

        stream.Write(isGoods); // isGoods
        if (isGoods)
        {
            stream.Write(0L); // freshnessTime
            stream.Write(0u); // type crafter?
            stream.Write((short)0); // type
        }
        return stream;
    }
}

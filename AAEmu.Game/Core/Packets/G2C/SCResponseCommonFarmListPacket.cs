using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResponseCommonFarmListPacket : GamePacket
{
    private readonly Dictionary<FarmType, List<Doodad>> _allPlanted;

    public SCResponseCommonFarmListPacket(Dictionary<FarmType, List<Doodad>> allPlanted)
        : base(SCOffsets.SCResponseCommonFarmListPacket, 5)
    {
        _allPlanted = allPlanted;
    }

    public override PacketStream Write(PacketStream stream)
    {
        //Need to send all data for all 4 tabs here!

        stream.Write(_allPlanted.Values.Sum(l => l.Count));
        stream.Write(_allPlanted.Values.Sum(l => l.Count));

        foreach (FarmType type in Enum.GetValues(typeof(FarmType)))
        {
            if (_allPlanted.TryGetValue(type, out var doodadList))
            {
                foreach (var doodad in doodadList)
                {
                    stream.Write((uint)type);
                    stream.Write(doodad.TemplateId);
                    stream.Write(doodad.TimeLeft);
                    stream.Write(doodad.FuncGroupId);
                    stream.WritePosition(doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, doodad.Transform.World.Position.Z);
                    stream.Write(doodad.PlantTime);
                }
            }
        }

        return stream;
    }
}

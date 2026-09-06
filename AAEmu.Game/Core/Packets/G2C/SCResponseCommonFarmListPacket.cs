using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResponseCommonFarmListPacket(Dictionary<FarmGroupKind, List<Doodad>> allPlanted)
    : GamePacket(SCOffsets.SCResponseCommonFarmListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        //Need to send all data for all 4 tabs here!

        stream.Write(allPlanted.Values.Sum(l => l.Count));
        stream.Write(allPlanted.Values.Sum(l => l.Count));

        foreach (var type in Enum.GetValues<FarmGroupKind>())
        {
            if (allPlanted.TryGetValue(type, out var doodadList))
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

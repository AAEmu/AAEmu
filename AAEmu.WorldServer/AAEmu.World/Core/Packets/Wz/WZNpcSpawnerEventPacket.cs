using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// type(i32), despawnOnCreatorDeath(bool), useSummonerAggroTarget(bool), lifeTime(f32).
/// </summary>
public sealed class WZNpcSpawnerEventPacket(WorldNpcSpawnerEventRequest request)
    : ZonePacket(WzOpcodes.NpcSpawnerEvent)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(request.SpawnerId);
        stream.Write((sbyte)request.Event);
        stream.Write((sbyte)NpcSpawnReasonType.Default);

        stream.Write((byte)request.CreatorType);
        switch (request.CreatorType)
        {
            case BaseUnitType.Character:
                stream.Write(request.CreatorCharacterId);
                stream.Write(request.CreatorValue);
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(request.CreatorObjId);
                stream.Write(request.CreatorTemplateId);
                stream.Write(request.CreatorOwnerId);
                stream.Write(request.CreatorFlag);
                break;
            default:
                throw new InvalidOperationException(
                    $"Creator identity type {request.CreatorType} was not validated before packet serialization.");
        }

        stream.Write((int)request.Type);
        stream.Write(request.DespawnOnCreatorDeath);
        stream.Write(request.UseSummonerAggroTarget);
        stream.Write(request.LifeTime);
    }
}

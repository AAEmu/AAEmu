using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.AI.Utils;

public static class AIUtils
{

    // This is taken from x2ai.lua
    public static Vector3 CalcNextRoamingPosition(NpcAi ai)
    {
        var maxRoamingDistance = 6;
        var newPosition = new Vector3(
            (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + ai.IdlePosition.X,
            (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + ai.IdlePosition.Y,
            ai.IdlePosition.Z);

        var terrainHeight = WorldManager.Instance.GetHeight(ai.Owner.Transform.ZoneId, newPosition.X, newPosition.Y);
        // Handles disabled heightmaps
        if (terrainHeight <= 0.0f || ai.Owner.CanFly)
            terrainHeight = newPosition.Z;

        if (newPosition.Z < terrainHeight && terrainHeight - maxRoamingDistance < newPosition.Z)
            newPosition.Z = terrainHeight;

        return newPosition;
    }

    //public static bool IsOutOfIdleArea(AbstractAI AI)
    //{
    //    var distToIdlePos = AAEmu.Game.Utils.MathUtil.CalculateDistance(AI.Owner.Transform.World.Position, AI.IdlePosition.Position);
    //    var range = 15;

    //    // if (isGroupMember)
    //    //     then
    //    //         range = 50;
    //    // end
    //    if (distToIdlePos > range)
    //        return true;
    //    return false;
    //}

    public static NpcAi GetAiByType(AiParamType type, Npc owner)
    {
        switch (type)
        {
            case AiParamType.AlmightyNpc:
                return new AlmightyNpcAiCharacter { Owner = owner };
            case AiParamType.ArcherHoldPosition:
                return new ArcherHoldPositionAiCharacter { Owner = owner };
            case AiParamType.ArcherRoaming:
                return new ArcherRoamingAiCharacter { Owner = owner };
            case AiParamType.BigMonsterRoaming:
                return new BigMonsterRoamingAiCharacter { Owner = owner };
            case AiParamType.BigMonsterHoldPosition:
                return new BigMonsterHoldPositionAiCharacter { Owner = owner };
            case AiParamType.Default:
                return new DefaultAiCharacter { Owner = owner };
            case AiParamType.Dummy:
                return new DummyAiCharacter { Owner = owner };
            case AiParamType.Flytrap:
                return new FlytrapAiCharacter { Owner = owner };
            case AiParamType.HoldPosition:
                return new HoldPositionAiCharacter { Owner = owner };
            case AiParamType.Roaming:
                return new RoamingAiCharacter { Owner = owner };
            case AiParamType.TowerDefenseAttacker:
                return new TowerDefenseAttackerAiCharacter { Owner = owner };
            case AiParamType.WildBoarHoldPosition:
                return new WildBoarHoldPositionAiCharacter { Owner = owner };
            case AiParamType.WildBoarRoaming:
                return new WildBoarRoamingAiCharacter { Owner = owner };
            default:
                return null;
        }
    }
}

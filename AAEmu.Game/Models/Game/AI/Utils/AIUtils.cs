using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.Utils;

public static class AiUtils
{

    // This is taken from x2ai.lua
    public static Vector3 CalcNextRoamingPosition(NpcAi ai)
    {
        var maxRoamingDistance = 6;
        var newPosition = new Vector3(
            (Random.Shared.NextSingle() - 0.5f) * maxRoamingDistance * 2 + ai.IdlePosition.X,
            (Random.Shared.NextSingle() - 0.5f) * maxRoamingDistance * 2 + ai.IdlePosition.Y,
            ai.IdlePosition.Z);

        // Get terrain height at new position; if no data available, keep idle Z
        var terrainZ = WorldManager.Instance.GetReferenceHeight(ai, newPosition.X, newPosition.Y, newPosition.Z, ai.Owner.Transform.ZoneId);
        newPosition.Z = terrainZ > 0f ? terrainZ : ai.IdlePosition.Z;

        return newPosition;
    }

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

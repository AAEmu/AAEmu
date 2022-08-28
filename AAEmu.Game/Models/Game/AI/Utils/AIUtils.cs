using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Params;
using AAEmu.Game.Models.Game.AI.UnitTypes;
using AAEmu.Game.Models.Game.AI.v2;
using AAEmu.Game.Models.Game.AI.v2.AiCharacters;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using Jace.Util;

namespace AAEmu.Game.Models.Game.AI.Utils
{
    public static class AIUtils
    {
        
        // This is taken from x2ai.lua
        public static Transform CalcNextRoamingPosition(NpcAi ai)
        {
            var idlePos = ai.IdlePosition.CloneDetached();
            var newPosition = idlePos.Clone();

            var maxRoamingDistance = 6;
            newPosition.Local.SetPosition(
                (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + idlePos.Local.Position.X,
                (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + idlePos.Local.Position.Y,
                idlePos.Local.Position.Z);

            var terrainHeight = WorldManager.Instance.GetHeight(newPosition.ZoneId, newPosition.Local.Position.X, newPosition.Local.Position.Y);
            // Handles disabled heightmaps
            if (terrainHeight <= 0.0f)
                terrainHeight = newPosition.Local.Position.Z;

            if (newPosition.Local.Position.Z < terrainHeight && terrainHeight - maxRoamingDistance < newPosition.Local.Position.Z)
                newPosition.Local.SetHeight(terrainHeight);

            return newPosition;
        }

        public static bool IsOutOfIdleArea(AbstractAI AI)
        {
            var distToIdlePos = AAEmu.Game.Utils.MathUtil.CalculateDistance(AI.Owner.Transform.World.Position, AI.IdlePosition.Position, true);
            var range = 15;
            
            // if (isGroupMember)
            //     then
            //         range = 50;
            // end
            if (distToIdlePos > range) 
                return true;
            return false;
        }

        public static NpcAi GetAiByType(AiParamType type, Npc owner)
        {
            switch (type)
            {
                case AiParamType.AlmightyNpc:
                    return new AlmightyNpcAiCharacter() {Owner = owner};
                case AiParamType.ArcherHoldPosition:
                    return new ArcherHoldPositionAiCharacter() {Owner = owner};  
                case AiParamType.ArcherRoaming:
                    return new ArcherRoamingAiCharacter() {Owner = owner};
                case AiParamType.BigMonsterRoaming:
                    return new BigMonsterRoamingAiCharacter() {Owner = owner};
                case AiParamType.BigMonsterHoldPosition:
                    return new BigMonsterHoldPositionAiCharacter() {Owner = owner};
                case AiParamType.Default:
                    return new DefaultAiCharacter() {Owner = owner};
                case AiParamType.Dummy:
                    return new DummyAiCharacter() {Owner = owner};
                case AiParamType.Flytrap:
                    return new FlytrapAiCharacter() {Owner = owner};
                case AiParamType.HoldPosition:
                    return new HoldPositionAiCharacter() {Owner = owner};
                case AiParamType.Roaming:
                    return new RoamingAiCharacter() {Owner = owner};
                case AiParamType.TowerDefenseAttacker:
                    return new TowerDefenseAttackerAiCharacter() {Owner = owner};
                case AiParamType.WildBoarHoldPosition:
                    return new WildBoarHoldPositionAiCharacter() {Owner = owner};
                case AiParamType.WildBoarRoaming:
                    return new WildBoarRoamingAiCharacter() {Owner = owner};
                default:
                    return null;
            }
        }
    }
}

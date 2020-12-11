using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.Utils
{
    public static class AIUtils
    {
        
        // This is taken from x2ai.lua
        public static Point CalcNextRoamingPosition(AbstractAI ai)
        {
            var idlePos = ai.IdlePosition;
            var newPosition = idlePos.Clone();

            var maxRoamingDistance = 6;
            newPosition.X = (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + idlePos.X;
            newPosition.Y = (Rand.NextSingle() - 0.5f) * maxRoamingDistance * 2 + idlePos.Y;
            newPosition.Z = idlePos.Z;

            var terrainHeight = WorldManager.Instance.GetHeight(newPosition.ZoneId, newPosition.X, newPosition.Y);
            // Handles disabled heightmaps
            if (terrainHeight <= 0.0f)
                terrainHeight = newPosition.Z;
            
            if (newPosition.Z < terrainHeight && terrainHeight - maxRoamingDistance < newPosition.Z)
                newPosition.Z = terrainHeight;
            
            return newPosition;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class SusManager : Singleton<SusManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public static string CategoryBot => "Bot";
    public static string CategoryCheating => "Cheat";
    public static string CategoryRmt => "RMT";
    
    private Dictionary<uint, (Vector3 pos, float skipTime)> LastPlayerPositions { get; } = [];
    private Dictionary<uint, (Vector3 pos, float skipTime)> LastPetPositions { get; } = [];
    
    // ReSharper disable once MemberCanBePrivate.Global
    public bool LogActivity(string category, uint accountId, uint playerId, uint zoneGroup, Vector3 position, string description)
    {
        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO audit_char_sus (sus_category, sus_account, sus_character, zone_group, x, y, z, description) " +
                "VALUES (@sus_category, @sus_account, @sus_character, @zone_group, @x, @y, @z, @description)";
            command.Parameters.AddWithValue("@sus_category", category);
            command.Parameters.AddWithValue("@sus_account", accountId);
            command.Parameters.AddWithValue("@sus_character", playerId);
            command.Parameters.AddWithValue("@zone_group", zoneGroup);
            command.Parameters.AddWithValue("@x", position.X);
            command.Parameters.AddWithValue("@y", position.Y);
            command.Parameters.AddWithValue("@z", position.Z);
            command.Parameters.AddWithValue("@description", description);
            command.Prepare();
            if (command.ExecuteNonQuery() <= 0)
            {
                Logger.Error($"Saving suspisious activity failed: {category} - {description}");
                return false;
            }
            Logger.Warn(description);
        }
        catch (Exception ex)
        {
            Logger.Error($"Saving suspisious activity failed: {category} - {description} - {ex}");
            return false;
        }
        return true;
    }

    public bool LogActivity(string category, Character player, string description)
    {
        return LogActivity(category, player?.AccountId ?? 0, player?.Id ?? 0, player?.Transform?.ZoneId ?? 0, player?.Transform?.World?.Position ?? Vector3.Zero, description);
    }
    
    public bool LogActivity(string category, string description)
    {
        return LogActivity(category, 0, 0, 0, Vector3.Zero, description);
    }

    #region movement_checks
    /// <summary>
    /// Do some analysis on player movement
    /// </summary>
    /// <param name="player"></param>
    /// <param name="deltaTime"></param>
    public void AnalyzePlayerDeltaMovement(Character player, float deltaTime)
    {
        if (!LastPlayerPositions.TryGetValue(player.Id, out var last))
        {
            LastPlayerPositions.Add(player.Id, (player.Transform.World.ClonePosition(), 0f));
            return;
        }
        
        var deltaPos = player.Transform.World.ClonePosition() - last.pos;
        var deltaFlatPos = deltaPos with { Z = 0 };

        if (player.IsRiding == false &&
            player.DisabledSetPosition == false &&
            deltaFlatPos != Vector3.Zero &&
            last.skipTime <= 0f)
        {
            var observedSpeed = deltaFlatPos.Length() / deltaTime;
            // var playerCheckSpeed = 5.0 * character.BaseMoveSpeed * character.MoveSpeedMul * 3.0;
            var playerCheckSpeed = 25.0;
            if (observedSpeed > playerCheckSpeed)
            {
                last.skipTime = 15f;
                // Looks like this player is moving a bit fast, better make a note of it
                LogActivity(CategoryCheating,
                    player,
                    $"Player {player.Name} seems to be moving a bit fast {observedSpeed:F1} m/s (max {playerCheckSpeed:F1})");
                // player.SendMessage($"Speed {observedSpeed:F1} m/s (max {playerCheckSpeed:F1}) !!!");
            }
            else
            {
                // player.SendMessage($"Speed {observedSpeed:F1} m/s (max {playerCheckSpeed:F1})");    
            }
        }

        last.skipTime -= deltaTime;
        LastPlayerPositions[player.Id] = (player.Transform.World.ClonePosition(), last.skipTime);
    }

    /// <summary>
    /// Resets current analysis of movement, call after things like teleport to prevent false positives
    /// </summary>
    /// <param name="playerId"></param>
    public void ResetAnalyzePlayerDeltaMovement(uint playerId)
    {
        _ = LastPlayerPositions.Remove(playerId);
    }

    /// <summary>
    /// Do some analysis on mount movement
    /// </summary>
    /// <param name="pet"></param>
    /// <param name="deltaTime"></param>
    public void AnalyzeMountDeltaMovement(Mate pet, float deltaTime)
    {
        if (!LastPetPositions.TryGetValue(pet.Id, out var last))
        {
            LastPetPositions.Add(pet.Id, (pet.Transform.World.ClonePosition(), 0f));
            return;
        }
        
        var deltaPos = pet.Transform.World.ClonePosition() - last.pos;
        var deltaFlatPos = deltaPos with { Z = 0 };

        if (pet.DisabledSetPosition == false &&
            deltaFlatPos != Vector3.Zero &&
            last.skipTime <= 0f)
        {
            var observedSpeed = deltaFlatPos.Length() / deltaTime;
            // var playerCheckSpeed = 5.0 * character.BaseMoveSpeed * character.MoveSpeedMul * 3.0;
            var playerCheckSpeed = 27.5;
            var petOwner = WorldManager.Instance.GetCharacterByObjId(pet.OwnerObjId);
            if (observedSpeed > playerCheckSpeed)
            {
                last.skipTime = 15f;
                // Looks like this player is moving a bit fast, better make a note of it
                LogActivity(CategoryCheating,
                    petOwner,
                    $"Pet {pet.Name} from {petOwner?.Name} seems to be moving a bit fast {observedSpeed:F1} m/s (max {playerCheckSpeed:F1})");
                // petOwner?.SendMessage($"Pet Speed {observedSpeed:F1} m/s (max {playerCheckSpeed:F1}) !!!");
            }
            else
            {
                // petOwner?.SendMessage($"Pet Speed {observedSpeed:F1} m/s (max {playerCheckSpeed:F1})");    
            }
        }

        last.skipTime -= deltaTime;
        LastPetPositions[pet.Id] = (pet.Transform.World.ClonePosition(), last.skipTime);
    }
    
    /// <summary>
    /// Resets current analysis of movement, call after things like teleport to prevent false positives
    /// </summary>
    /// <param name="petId"></param>
    public void ResetAnalyzeMountDeltaMovement(uint petId)
    {
        _ = LastPetPositions.Remove(petId);
    }
    #endregion
}

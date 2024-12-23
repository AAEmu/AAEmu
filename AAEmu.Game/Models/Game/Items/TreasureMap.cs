using System;
using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class TreasureMap : Item
{
    private long _mapPositionX;
    private long _mapPositionY;

    public double MapPositionX
    {
        get => _mapPositionX / 4096.0;
        set => _mapPositionX = (long)Math.Round(value * 4096.0);
    }

    public double MapPositionY
    {
        get => _mapPositionY / 4096.0;
        set => _mapPositionY = (long)Math.Round(value * 4096.0);
    }

    public override ItemDetailType DetailType => ItemDetailType.Treasure;
    public override uint DetailBytesLength => 24;

    /// <summary>
    /// Generates ItemDetails data based on possible spawn locations
    /// </summary>
    private void GenerateTreasureMapData()
    {
        // Pick a random target
        // TODO: Update this to use the spawners rather than the Doodads
        var possibleChests = SpawnManager.Instance.GetTreasureChestDoodadSpawners();
        if (possibleChests.Count <= 0)
        {
            // No valid location found
            // Let's mark it Mythic for fun as it'll be at 0, 0 very much out of bounds if it isn't from loading items
            Grade = (byte)ItemGrade.Mythic;
            return;
        }
        var rngPos = Random.Shared.Next(possibleChests.Count);
        var chest = possibleChests[rngPos];
        // Setup coordinates
        MapPositionX = chest.Position.X;
        MapPositionY = chest.Position.Y;
        
        var chestTemplate = DoodadManager.Instance.GetTemplate(chest.UnitId) ?? DoodadManager.Instance.GetTemplate(chest.RespawnDoodadTemplateId);
        var chestGradeId = chestTemplate?.GroupId ?? 0; 
        // Give it a corresponding grade
        switch (chestGradeId)
        {
            case 55: Grade = 0;
                break;
            case 56: Grade = (byte)ItemGrade.Grand;
                break;
            case 57: Grade = (byte)ItemGrade.Rare;
                break;
            case 58: Grade = (byte)ItemGrade.Arcane;
                break;
            case 59: Grade = (byte)ItemGrade.Heroic;
                break;
            default:
                // Invalid category
                Grade = (byte)ItemGrade.Legendary;
                Logger.Error($"Generated treasure map data for chest {chestTemplate?.Id} did not have a valid category for chests ({chestTemplate?.GroupId})");
                break;
        }
    }

    public TreasureMap()
    {
        // GenerateTreasureMapData();
    }

    public TreasureMap(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GenerateTreasureMapData();
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        var unknown1 = stream.ReadInt64();
        _mapPositionX = stream.ReadInt64();
        _mapPositionY = stream.ReadInt64();
    }

    public override void WriteDetails(PacketStream stream)
    {
        // MapPositionX = 21504 * 4096; // E 0
        // MapPositionY = 28672 * 4096; // N 0
        stream.Write((long)0);
        stream.Write(_mapPositionX);
        stream.Write(_mapPositionY);
    }

    public Vector3 GetMapPosition(float z)
    {
        return new Vector3((float)MapPositionX, (float)MapPositionY, z);
    }
}

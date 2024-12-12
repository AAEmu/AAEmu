using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

internal class IndunEventNoAliveChInRooms : IndunEvent
{
    public uint RoomId { get; set; }
    private Dictionary<uint, uint> _playerRoomCount;
    private Dictionary<uint, Doodad> _doodads;

    public IndunEventNoAliveChInRooms()
    {
        _playerRoomCount = [];
        _doodads = [];
    }

    public override void Subscribe(InstanceWorld world)
    {
        var doodadList = new List<Doodad>();
        var indunRoom = IndunGameData.Instance.GetRoom(RoomId);
        foreach (var region in world.Regions)
        {
            region.GetList(doodadList, 0);
        }
        doodadList = doodadList.Where(doodad => doodad.TemplateId == indunRoom.DoodadId).ToList();
        if (doodadList.Count > 0)
        {
            if (doodadList.Count > 1)
                Logger.Warn("[IndunEvent] DoodadList returned higher than one doodad count.");

            if (_doodads.TryGetValue(world.Id, out _))
            {
                _doodads[world.Id] = doodadList[0];
            }
            else
            {
                _doodads.Add(world.Id, doodadList[0]);
            }
            if (_playerRoomCount.TryGetValue(world.Id, out _))
            {
                _playerRoomCount[world.Id] = 0;
            }
            else
            {
                _playerRoomCount.Add(world.Id, 0);
            }
            world.Events.OnAreaClear += OnAreaClear;
        }
    }

    public override void UnSubscribe(InstanceWorld world)
    {
        _doodads.Remove(world.Id);
        _playerRoomCount.Remove(world.Id);
        world.Events.OnAreaClear -= OnAreaClear;
    }

    public uint GetRoomPlayerCount(uint instanceId)
    {
        if (_playerRoomCount.TryGetValue(instanceId, out var value))
            return value;

        throw new KeyNotFoundException("Key not found for RoomPlayerCount.");
    }

    public void SetRoomPlayerCount(uint instanceId, uint count)
    {
        _playerRoomCount[instanceId] = count;
    }

    public Doodad GetRoomDoodad(uint worldId)
    {
        Logger.Warn($"GetRoomDoodad, world {worldId}");
        if (_doodads.TryGetValue(worldId, out var value))
        {
            Logger.Warn($"RoomDoodad found templateId={value.TemplateId}");
            return value;
        }

        Logger.Warn($"RoomDoodad not found, world {worldId}");
        return null;
    }

    private void OnAreaClear(object sender, OnAreaClearArgs args)
    {
        if (sender is InstanceWorld world)
        {
            Logger.Warn($"OnAreaClear, world {world.Id}");

        }
    }
}

using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

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

    public override void Subscribe(WorldInstance worldInstance)
    {
        var doodadList = new List<Doodad>();
        var indunRoom = IndunGameData.Instance.GetRoom(RoomId);
        foreach (var region in worldInstance.Regions)
        {
            region.GetList(doodadList, 0);
        }
        doodadList = doodadList.Where(doodad => doodad.TemplateId == indunRoom.DoodadId).ToList();
        if (doodadList.Count > 0)
        {
            if (doodadList.Count > 1)
                Logger.Warn("[IndunEvent] DoodadList returned higher than one doodad count.");

            if (_doodads.TryGetValue(worldInstance.Id, out _))
            {
                _doodads[worldInstance.Id] = doodadList[0];
            }
            else
            {
                _doodads.Add(worldInstance.Id, doodadList[0]);
            }
            if (_playerRoomCount.TryGetValue(worldInstance.Id, out _))
            {
                _playerRoomCount[worldInstance.Id] = 0;
            }
            else
            {
                _playerRoomCount.Add(worldInstance.Id, 0);
            }
            worldInstance.Events.OnAreaClear += OnAreaClear;
        }
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        _doodads.Remove(worldInstance.Id);
        _playerRoomCount.Remove(worldInstance.Id);
        worldInstance.Events.OnAreaClear -= OnAreaClear;
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
        if (sender is WorldInstance world)
        {
            Logger.Warn($"OnAreaClear, world {world.Id}");

        }
    }
}

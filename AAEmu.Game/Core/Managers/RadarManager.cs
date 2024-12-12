using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers;

public class RadarManager : Singleton<RadarManager>
{
    private int RadarUpdateDelay { get; } = 1000;
    private static object Lock { get; } = new();
    private Dictionary<uint, TelescopeRegistrationEntry> Registrations { get; set; }
    private static int TransfersPerPacket { get; } = 10;
    private static int FishPerPacket { get; } = 10;
    private static int ShipsPerPacket { get; } = 10;

    public void Initialize()
    {
        Registrations = [];
        TickManager.Instance.OnTick.Subscribe(RadarTick, TimeSpan.FromMilliseconds(RadarUpdateDelay), true);
    }

    public void RegisterForPublicTransport(Character player, float checkRange)
    {
        lock (Lock)
        {
            // If no entry, create one
            if (Registrations.TryGetValue(player.Id, out var entry))
            {
                entry.ShowPublicTransportRange = checkRange;
            }
            else
            {
                entry = new TelescopeRegistrationEntry()
                {
                    Player = player,
                    ShowPublicTransportRange = checkRange,
                };
                Registrations.Add(player.Id, entry);
            }

            // If nothing set, delete
            if (entry.IsActive == false)
                Registrations.Remove(entry.Player.Id);
        }
    }

    public void RegisterForFishSchool(Character player, float checkRange)
    {
        lock (Lock)
        {
            // If no entry, create one
            if (Registrations.TryGetValue(player.Id, out var entry))
            {
                entry.ShowFishSchoolRange = checkRange;
            }
            else
            {
                entry = new TelescopeRegistrationEntry()
                {
                    Player = player,
                    ShowFishSchoolRange = checkRange,
                };
                Registrations.Add(player.Id, entry);
            }

            // If nothing set, delete
            if (entry.IsActive == false)
                Registrations.Remove(entry.Player.Id);
        }
    }

    public void RegisterForShips(Character player, float checkRange)
    {
        lock (Lock)
        {
            // If no entry, create one
            if (Registrations.TryGetValue(player.Id, out var entry))
            {
                entry.ShowShipTelescopeRange = checkRange;
            }
            else
            {
                entry = new TelescopeRegistrationEntry()
                {
                    Player = player,
                    ShowShipTelescopeRange = checkRange,
                };
                Registrations.Add(player.Id, entry);
            }

            // If nothing set, delete
            if (entry.IsActive == false)
                Registrations.Remove(entry.Player.Id);
        }
    }

    public void RadarTick(TimeSpan delta)
    {
        lock (Lock)
        {
            if (Registrations.Count <= 0)
                return;

            var allTransfers = TransferManager.Instance.GetTransfers();
            var allFish = FishSchoolManager.Instance.GetAllFishSchools();
            // TODO: Add Shipyards
            var allShips = SlaveManager.Instance.GetActiveSlavesByKinds(
            [
                SlaveKind.Boat, SlaveKind.Fishboat, SlaveKind.Speedboat, SlaveKind.MerchantShip,
                SlaveKind.BigSailingShip, SlaveKind.SmallSailingShip
            ]).ToList();

            foreach (var (_, entry) in Registrations)
            {
                // TODO: Make a proper GM flag
                var gmRangeCheck = CharacterManager.Instance.GetEffectiveAccessLevel(entry.Player) >= 100 ? 100000f : 0f;
                if (entry.Player == null)
                    continue;

                // Check public Transportation
                if (entry.ShowPublicTransportRange > 0)
                {
                    var inRangeTransfers = new List<Transfer>();
                    foreach (var transfer in allTransfers)
                    {
                        // Ignore Carriage Boardings
                        if (transfer.TemplateId == 46)
                            continue;

                        if ((transfer.Transform.WorldId != entry.Player.Transform.WorldId) || (transfer.Transform.InstanceId != entry.Player.Transform.InstanceId))
                            continue;

                        if (MathUtil.CalculateDistance(entry.Player, transfer, true) <= Math.Max(entry.ShowPublicTransportRange, gmRangeCheck))
                        {
                            inRangeTransfers.Add(transfer);
                        }
                    }

                    // Send Data
                    if (inRangeTransfers.Count > 0)
                    {
                        for (var i = 0; i < inRangeTransfers.Count; i += TransfersPerPacket)
                        {
                            var last = inRangeTransfers.Count - i <= TransfersPerPacket;
                            var temp = inRangeTransfers.GetRange(i, last ? inRangeTransfers.Count - i : TransfersPerPacket).ToArray();
                            entry.Player.SendPacket(new SCTransferTelescopeUnitsPacket(last, temp));
                        }
                    }
                }

                // Check for Fish Schools
                if (entry.ShowFishSchoolRange > 0)
                {
                    var inRangeFish = new List<Doodad>();
                    foreach (var fish in allFish)
                    {
                        if ((fish.Transform.WorldId != entry.Player.Transform.WorldId) || (fish.Transform.InstanceId != entry.Player.Transform.InstanceId))
                            continue;

                        if (MathUtil.CalculateDistance(entry.Player, fish, true) <= Math.Max(entry.ShowFishSchoolRange, gmRangeCheck))
                        {
                            inRangeFish.Add(fish);
                        }
                    }

                    // Send Data
                    if (inRangeFish.Count > 0)
                    {
                        for (var i = 0; i < inRangeFish.Count; i += FishPerPacket)
                        {
                            var last = inRangeFish.Count - i <= FishPerPacket;
                            var temp = inRangeFish.GetRange(i, last ? inRangeFish.Count - i : FishPerPacket).ToArray();
                            entry.Player.SendPacket(new SCSchoolOfFishDoodadsPacket(last, temp));
                        }
                    }
                }

                // Check for All Ships
                if (entry.ShowShipTelescopeRange > 0)
                {
                    var inRangeShips = new List<Slave>();
                    foreach (var ship in allShips)
                    {
                        if ((ship.Transform.WorldId != entry.Player.Transform.WorldId) || (ship.Transform.InstanceId != entry.Player.Transform.InstanceId))
                            continue;

                        if (MathUtil.CalculateDistance(entry.Player, ship, true) <= Math.Max(entry.ShowShipTelescopeRange, gmRangeCheck))
                        {
                            inRangeShips.Add(ship);
                        }
                    }

                    // Send Data
                    if (inRangeShips.Count > 0)
                    {
                        for (var i = 0; i < inRangeShips.Count; i += ShipsPerPacket)
                        {
                            var last = inRangeShips.Count - i <= ShipsPerPacket;
                            var temp = inRangeShips.GetRange(i, last ? inRangeShips.Count - i : ShipsPerPacket).ToArray();
                            entry.Player.SendPacket(new SCTelescopeUnitsPacket(last, temp));
                        }
                    }
                }

            } // for each player
        } // lock
    }

    public void UnRegister(Character player)
    {
        lock (Lock)
        {
            Registrations.Remove(player.Id);
        }
    }
}

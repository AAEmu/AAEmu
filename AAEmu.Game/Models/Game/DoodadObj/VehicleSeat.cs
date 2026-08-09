using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj;

/// <summary>
/// Here is the work with the places in the cart
/// </summary>

public class VehicleSeat(BaseUnit parentVehicle)
{
    // objId Doodad - Chair, bench, bed where we sit down or lay
    // List<character.Id> List of employed places on a chair, bench, beds, or 0, if the place is free
    private readonly Dictionary<uint, List<uint>> _seats = []; // objId, List<character.Id>

    // Space = 1-means that there is one place (a chair), Space = 2-means that there are two places to sit (a bench on transport)
    // Spot = 0 sit left, = 1 sit right on the bench

    /// <summary>
    /// Seats are owned by a doodad (`new VehicleSeat(this)`), not the transfer. Resolve any unit carrier.
    /// </summary>
    private BaseUnit ResolveCarrier()
    {
        if (parentVehicle is Transfer or Slave)
            return parentVehicle;
        if (parentVehicle is Doodad seat)
            return BondDoodad.ResolveCarrierUnit(seat);
        return null;
    }

    private void Init(uint objId, int space)
    {
        var tmp = new List<uint>();
        for (var i = 0; i < space; i++)
        {
            tmp.Add(0); // No one took a place
        }
        _seats.Add(objId, tmp); // Add a list with empty places
    }

    private void AddSeat(Character character, uint seatObjId, int spot)
    {
        _seats[seatObjId][spot] = character.Id;
        // Parenting to the carrier is applied by DoodadFuncAttachment after bond setup.
        // Track transfer passengers for bookkeeping only.
        if (ResolveCarrier() is Transfer transfer && !transfer.AttachedCharacters.Contains(character))
            transfer.AttachedCharacters.Add(character);
    }

    public void UnLoadPassenger(Character character, uint seatObjId)
    {
        if (!_seats.TryGetValue(seatObjId, out var spots))
            return;

        for (var i = 0; i < spots.Count; i++)
        {
            if (spots[i] == character.Id)
            {
                spots[i] = 0; // free up space
                character.Transform.StickyParent = null;
                if (ResolveCarrier() is Transfer transfer)
                    transfer.AttachedCharacters.Remove(character);
            }
        }
    }

    private int GetFreeSeat(uint seatObjId)
    {
        if (!_seats.TryGetValue(seatObjId, out var value)) { return -1; }

        for (var i = 0; i < value.Count; i++)
        {
            if (_seats[seatObjId][i] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    public int LoadPassenger(Character character, uint seatObjId, int space)
    {
        if (!_seats.ContainsKey(seatObjId))
        {
            Init(seatObjId, space);
        }

        var spot = GetFreeSeat(seatObjId);
        if (spot == -1)
        {
            return spot;
        }

        if (spot < space)
        {
            AddSeat(character, seatObjId, spot);
        }
        else
        {
            spot = -1;
        }

        return spot;
    }
}

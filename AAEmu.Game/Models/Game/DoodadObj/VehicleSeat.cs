using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    /// <summary>
    /// Here is the work with the places in the cart
    /// </summary>

    public class VehicleSeat
    {
        // objId Doodad - Chair, bench, bed where we sit down or lay
        // List<character.Id> List of employed places on a chair, bench, beds, or 0, if the place is free
        private Dictionary<uint, List<uint>> _seats;

        // Space = 1-means that there is one place (a chair), Space = 2-means that there are two places to sit (a bench on transport)
        // Spot = 0 sit left, = 1 sit right on the bench

        public VehicleSeat()
        {
            _seats = new Dictionary<uint, List<uint>>(); // objId, List<character.Id>
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

        private void AddSeat(uint characterId, uint seatObjId, int spot)
        {
            _seats[seatObjId][spot] = characterId; // occupied place
        }
        public void UnLoadPassenger(uint characterId, uint seatObjId)
        {
            for (var i = 0; i < _seats[seatObjId].Count; i++)
            {
                if (_seats[seatObjId][i] == characterId)
                {
                    _seats[seatObjId][i] = 0; // free up space
                }
            }
        }

        private int GetFreeSeat(uint seatObjId)
        {
            if (!_seats.ContainsKey(seatObjId)) { return -1; }

            for (var i = 0; i < _seats[seatObjId].Count; i++)
            {
                if (_seats[seatObjId][i] == 0)
                {
                    return i;
                }
            }

            return -1;
        }

        public int LoadPassenger(uint characterId, uint seatObjId, int space)
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
                AddSeat(characterId, seatObjId, spot);
            }
            else
            {
                spot = -1;
            }

            return spot;
        }
    }
}


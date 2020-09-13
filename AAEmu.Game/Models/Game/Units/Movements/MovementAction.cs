/*
   Author:Sagara
*/

using System.Numerics;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class MovementAction
    {
        public Point _startPosition, _endPosition;
        public Vector3 _startPos, _endPos;
        public float _cos, _sin;
        public sbyte _heading;
        public UnitMovementType _movementType;
        public short _speed;

        public MovementAction(Point startPosition, Point endPosition, sbyte heading, short speed, UnitMovementType unitMovementType)
        {
            _heading = heading;
            _startPosition = startPosition;
            _endPosition = endPosition;
            _movementType = unitMovementType;
            _speed = speed;
        }
        public MovementAction(Vector3 startPosition, Vector3 endPosition, sbyte heading, short speed, UnitMovementType unitMovementType)
        {
            _heading = heading;
            _startPos = startPosition;
            _endPos = endPosition;
            _movementType = unitMovementType;
            _speed = speed;
        }
    }
}

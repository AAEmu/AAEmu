/*
   Author:Sagara
*/

using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Units.Movements
{
    public class MovementAction
    {
        public Point _startPosition, _endPosition;
        public float _cos, _sin;
        public sbyte _heading;
        public MoveTypeEnum _movementType;
        public short _speed;

        public MovementAction(Point startPosition, Point endPosition, sbyte heading, short speed, MoveTypeEnum moveType)
        {
            _heading = heading;
            _startPosition = startPosition;
            _endPosition = endPosition;
            _movementType = moveType;
            _speed = speed;
        }
    }
}

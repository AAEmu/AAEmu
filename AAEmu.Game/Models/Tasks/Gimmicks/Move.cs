using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Models.Tasks.Gimmicks
{
    public class Move : Task
    {
        private readonly Simulation _patrol;
        private readonly Npc _npc;
        private readonly Transfer _transfer;
        private readonly float _targetX;
        private readonly float _targetY;
        private readonly float _targetZ;
        private readonly bool _isNpc;

        /// <summary>
        /// Move task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="unit"></param>
        /// <param name="TargetX"></param>
        /// <param name="TargetY"></param>
        /// <param name="TargetZ"></param>
        public Move(Simulation patrol, Unit unit, float TargetX, float TargetY, float TargetZ)
        {
            _patrol = patrol;
            _targetX = TargetX;
            _targetY = TargetY;
            _targetZ = TargetZ;
            if (unit is Npc npc)
            {
                _npc = npc;
                _isNpc = true;
            }
            if (unit is Transfer transfer)
            {
                _transfer = transfer;
                _isNpc = false;
            }
        }

        /// <summary>
        /// Perform tasks
        /// </summary>
        public override void Execute()
        {
            if (_isNpc)
            {
                if (_npc.Hp > 0)
                {
                    _patrol?.MoveTo(_patrol, _npc, _targetX, _targetY, _targetZ);
                }
            }
            else
            {
                if (_transfer.Hp > 0)
                {
                    _patrol?.MoveTo(_patrol, _transfer, _targetX, _targetY, _targetZ);
                }
            }
        }
    }
}

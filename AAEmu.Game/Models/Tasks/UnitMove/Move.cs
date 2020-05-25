using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class Move : Task
    {
        private readonly Simulation _patrol;
        private readonly Npc _npc;
        private readonly float _targetX;
        private readonly float _targetY;
        private readonly float _targetZ;

        /// <summary>
        /// Initialization task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="ch"></param>
        public Move(Simulation patrol, Npc npc, float TargetX, float TargetY, float TargetZ)
        {
            _patrol = patrol;
            _npc = npc;
            _targetX = TargetX;
            _targetY = TargetY;
            _targetZ = TargetZ;
        }

        /// <summary>
        /// Perform tasks
        /// </summary>
        public override void Execute()
        {
            if (_npc.Hp > 0)
            {
                _patrol?.MoveTo(_patrol, _npc, _targetX, _targetY, _targetZ);
            }
        }
    }
}

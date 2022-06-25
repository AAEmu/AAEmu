using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Models.Tasks.UnitMove
{
    public class Record : Task
    {
        private readonly Simulation _patrol;
        private readonly ICharacter _ch;

        /// <summary>
        /// Initialization task
        /// </summary>
        /// <param name="patrol"></param>
        /// <param name="ch"></param>
        public Record(Simulation sim, ICharacter ch)
        {
            _patrol = sim;
            _ch = ch;
        }

        /// <summary>
        /// Perform tasks
        /// </summary>
        public override void Execute()
        {
            if (_ch.Hp > 0)
            {
                _patrol?.Record(_patrol, _ch);
            }
        }
    }
}

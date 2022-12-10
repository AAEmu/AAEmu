using System.Text;

using NLua;

namespace AAEmu.Game.Models.Game.AI.Params
{
    class AiLua : Lua
    {
        public AiLua() : base()
        {
            //Define constants - Pseudo Values are used here
            StringBuilder aiConsts = new StringBuilder();
            //UseTypes
            aiConsts.Append("USE_SEQUENCE = 1;");
            aiConsts.Append("USE_RANDOM = 2;");


            //TargetTypes
            aiConsts.Append("AGGRO_TOTAL = 1;");
            aiConsts.Append("AGGRO_HEAL = 2;");

            //PhaseChangeTypes
            aiConsts.Append("PHASE_TYPE_NONE = 0;");
            aiConsts.Append("PHASE_TYPE_SEQUENCE = 1;");

            //PhaseTypes
            aiConsts.Append("PHASE_DRAGON_GROUND = 1;");
            aiConsts.Append("PHASE_DRAGON_HOVERING = 2;");


            DoString(aiConsts.ToString());
        }
    }
}

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncClimateReact : DoodadFuncTemplate
    {
        public uint NextPhase { get; set; }

        //public override async void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncClimateReact");

            // if (owner.FuncTask != null)
            // {
            //     await owner.FuncTask.Cancel();
            //     owner.FuncTask = null;
            // }
            //
            // owner.FuncGroupId = NextPhase;
            // var funcs = DoodadManager.Instance.GetPhaseFunc(owner.FuncGroupId);
            // foreach (var func in funcs)
            //     func.Use(caster, owner, skillId);
            // owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), true);
            if (caster is Character)
            {
                //I think this is used to reschedule anything that needs triggered at a specific gametime
                owner.OverridePhase = NextPhase;
                owner.ToPhaseAndUse = true;
            }
            owner.ToPhaseAndUse = false;
        }
    }
}

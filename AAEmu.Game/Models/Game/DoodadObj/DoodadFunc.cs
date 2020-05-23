using System;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadFunc
    {

        private static Logger _log = LogManager.GetCurrentClassLogger();
        public uint GroupId { get; set; }
        public uint FuncId { get; set; }
        public uint FuncKey { get; set; }
        public string FuncType { get; set; }
        public int NextPhase { get; set; }
        public uint SoundId { get; set; }
        public uint SkillId { get; set; }
        public uint PermId { get; set; }
        public int Count { get; set; }

        //This gets called after a world -> doodad interaction
        public async void Use(Unit caster, Doodad owner, uint skillId)
        {
            var template = DoodadManager.Instance.GetFuncTemplate(FuncId, FuncType);

            if (template == null)
                return;

            template.Use(caster, owner, skillId);

            if (NextPhase > 0)
            {
                if (owner.FuncTask != null)
                {
                    await owner.FuncTask.Cancel();
                    owner.FuncTask = null;
                }

                owner.FuncGroupId = (uint)NextPhase;

                owner.BroadcastPacket(new SCDoodadPhaseChangedPacket(owner), false); // FIX: added to work on/off lighting and destruction of drums/boxes

                var funcs = DoodadManager.Instance.GetPhaseFunc(owner.FuncGroupId);
                foreach (var func in funcs)
                    func.Use(caster, owner, skillId);
            }
        }
    }
}

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Char
{
    public partial class Character
    {
        public uint ResurrectHpPercent { get; set; } = 1;
        public uint ResurrectMpPercent { get; set; } = 1;
        public uint HostileFactionKills { get; set; }
        public uint HonorGainedInCombat { get; set; }

        public override void DoDie(Unit killer, KillReason killReason)
        {
            base.DoDie(killer, killReason);

            if (killer is Character enemy && enemy.Faction.MotherId != Faction.MotherId)
                enemy.HostileFactionKills++;
        }
    }
}

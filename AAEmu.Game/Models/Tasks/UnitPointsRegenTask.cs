using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Tasks;

public class UnitPointsRegenTask(Unit unit) : Task
{
    public override void Execute()
    {
        var oldHp = unit.Hp;
        if (unit.Hp < unit.MaxHp && unit.Hp > 0)
            unit.Hp += unit.HpRegen; // TODO at battle _unit.PersistentHpRegen
        if (unit.Mp < unit.MaxMp && unit.Hp > 0)
            unit.Mp += unit.MpRegen; // TODO at battle _unit.PersistentMpRegen
        unit.Hp = Math.Min(unit.Hp, unit.MaxHp);
        unit.Mp = Math.Min(unit.Mp, unit.MaxMp);
        unit.BroadcastPacket(new SCUnitPointsPacket(unit.ObjId, unit.Hp, unit.Mp, unit.HighAbilityRsc), true);
        unit.PostUpdateCurrentHp(unit, oldHp, unit.Hp, KillReason.Unknown);
        //if (_unit.Hp >= _unit.MaxHp && _unit.Mp >= _unit.MaxMp)
        //    _unit.StopRegen();
    }
}

using System;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks
{
    public class UnitPointsRegenTask : Task
    {
        private Unit _unit;

        public UnitPointsRegenTask(Unit unit)
        {
            _unit = unit;
        }

        public override void Execute()
        {
            if (_unit.Hp < _unit.MaxHp && _unit.Hp>0)
                _unit.Hp += _unit.HpRegen; // TODO at battle _unit.PersistentHpRegen
            if (_unit.Mp < _unit.MaxMp && _unit.Hp > 0)
                _unit.Mp += _unit.MpRegen; // TODO at battle _unit.PersistentMpRegen
            _unit.Hp = Math.Min(_unit.Hp, _unit.MaxHp);
            _unit.Mp = Math.Min(_unit.Mp, _unit.MaxMp);
            _unit.BroadcastPacket(new SCUnitPointsPacket(_unit.ObjId, _unit.Hp, _unit.Mp), true);
            if (_unit.Hp >= _unit.MaxHp && _unit.Mp >= _unit.MaxMp)
                _unit.StopRegen();
        }
    }
}

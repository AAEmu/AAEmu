using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCGmDumpSubStatPacket(string name, float meleeDps, float rangedDps, float spellDps, float healDps, float armor, float armorPercentage, float magicResist, float magicResistPercentage, float moveSpeed, float castingTime, float mainhandMeleeSpeed, float offhandMeleeSpeed, float meleeSuccessRate, float meleeCriticalRate, float rangedSpeed, float rangedSuccessRate, float rangedCriticalRate, float bullsEye, float spellSuccessRate, float spellCriticalRate, float healCriticalRate, float meleeParryRate, float rangedParryRate, float blockRate, float dodgeRate, float flexibility, float battleResist, float healthRegen, float persistentHealthRegen, float manaRegen, float persistentManaRegen, float str, float dex, float sta, float @int, float spi, float meleeMinDps, float meleeMaxDps, float rangedMinDps, float rangedMaxDps, float castingTimeMul, float magicEffectResistPercentage, ulong maxHealth, ulong maxMana, float globalCoolDown, float attackAnimSpeed, float gearScore, float bareGearScore) : GamePacket(SCOffsets.SCGmDumpSubStatPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(name);
        stream.Write(meleeDps);
        stream.Write(rangedDps);
        stream.Write(spellDps);
        stream.Write(healDps);
        stream.Write(armor);
        stream.Write(armorPercentage);
        stream.Write(magicResist);
        stream.Write(magicResistPercentage);
        stream.Write(moveSpeed);
        stream.Write(castingTime);
        stream.Write(mainhandMeleeSpeed);
        stream.Write(offhandMeleeSpeed);
        stream.Write(meleeSuccessRate);
        stream.Write(meleeCriticalRate);
        stream.Write(rangedSpeed);
        stream.Write(rangedSuccessRate);
        stream.Write(rangedCriticalRate);
        stream.Write(bullsEye);
        stream.Write(spellSuccessRate);
        stream.Write(spellCriticalRate);
        stream.Write(healCriticalRate);
        stream.Write(meleeParryRate);
        stream.Write(rangedParryRate);
        stream.Write(blockRate);
        stream.Write(dodgeRate);
        stream.Write(flexibility);
        stream.Write(battleResist);
        stream.Write(healthRegen);
        stream.Write(persistentHealthRegen);
        stream.Write(manaRegen);
        stream.Write(persistentManaRegen);
        stream.Write(str);
        stream.Write(dex);
        stream.Write(sta);
        stream.Write(@int);
        stream.Write(spi);
        stream.Write(meleeMinDps);
        stream.Write(meleeMaxDps);
        stream.Write(rangedMinDps);
        stream.Write(rangedMaxDps);
        stream.Write(castingTimeMul);
        stream.Write(magicEffectResistPercentage);
        stream.Write(maxHealth);
        stream.Write(maxMana);
        stream.Write(globalCoolDown);
        stream.Write(attackAnimSpeed);
        stream.Write(gearScore);
        stream.Write(bareGearScore);
        return stream;
    }
}

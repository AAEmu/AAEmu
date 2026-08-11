using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpawnCharacterPacket() : GamePacket(CSOffsets.CSSpawnCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        if (Connection.ActiveChar == null)
        {
            Logger.Warn("CSSpawnCharacterPacket: no ActiveChar (account {0}) — ignored", Connection.AccountId);
            return;
        }

        Connection.State = GameState.World;

        Connection.ActiveChar.LastPacketActivityTime = DateTime.UtcNow;
        Connection.ActiveChar.VisualOptions = new CharacterVisualOptions();
        Connection.ActiveChar.VisualOptions.Read(stream);

        // 10.0.2.13: bind the client's own player unit. RUNTIME-VERIFIED via the crash dump
        // charId equals the active character id. Without the player's own SCUnitState the unit stays null and the
        // matches the client's active slot, so the client recognizes it as MyUnit and binds it.
        var me = Connection.ActiveChar;
        Connection.SendPacket(new SCUnitStatePacket(me));
        Connection.SendPacket(new SCListSkillActiveTypePacket(me.SkillActiveTypes.BuildPacketEntries()));
        Connection.SendPacket(new SCHeirSkillListPacket(me.HeirSkills.BuildPacketEntries()));
        // If UnitState left the local unit at 0, (Id,Id) no-ops — send old=0 → new=real.
        if (me.Faction != null && (uint)me.Faction.Id != 0)
            Connection.SendPacket(new SCUnitFactionChangedPacket(
                me.ObjId, me.Name ?? "", FactionsEnum.Invalid, me.Faction.Id, false));

        Connection.SendPacket(new SCDetailedTimeOfDayPacket(
            TimeManager.Instance.GetTime, TimeManager.DefaultGameHourSpeed, 0f, 24f));

        // Deliberately no labor packet here. SCCharacterLaborPowerChanged is a delta - the handler does
        // `add [mgr+0xE58], amount` and `add [mgr+0xE68], localAmount`, never a store - and the client
        // already carries both balances into the world from the {lp, localLp, ...} block in the
        // character-list record. Sending the totals once more would add them on top, so the in-world
        // display reads exactly twice the stored balance. Only real changes belong on this packet.

        // After SCUnitState, so the client has the unit before the buff-created packet references it.
        me.ApplyPremiumGradeBuff();

        Logger.Info("CSSpawnCharacterPacket faction={0} premiumGrade={1}", me.Faction?.Id, me.PremiumGrade);
    }
}

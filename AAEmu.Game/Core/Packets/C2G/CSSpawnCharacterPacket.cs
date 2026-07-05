using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Observers;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSpawnCharacterPacket() : GamePacket(CSOffsets.CSSpawnCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Connection.State = GameState.World;

        Connection.ActiveChar.VisualOptions = new CharacterVisualOptions();
        Connection.ActiveChar.VisualOptions.Read(stream);

        // 10.0.2.13: bind the client's own player unit. The local unit pointer is written only by the client's
        // BindMyUnit path ("client my unit is bound!"), whose sole caller is the SCUnitState handler — and only
        // when the descriptor's charId equals the active character id. Without the player's own SCUnitState the
        // unit stays null and the post-NotifyInGame delayed play logic null-derefs it. The charId here
        // (ActiveChar.Id) matches the client's active slot, so the client recognizes it as MyUnit and binds it.
        Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));

        Connection.ActiveChar.PushSubscriber(
            TimeManager.Instance.Subscribe(Connection, new TimeOfDayObserver(Connection.ActiveChar))
        );

        Logger.Info("CSSpawnCharacterPacket");
    }
}

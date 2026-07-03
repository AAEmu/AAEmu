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

        // 10.0.2.13: the client's own character is bound into the world via the CryNetwork context on
        // CSSpawnCharacter (the state machine advances FinishState(6) -> ChangeState(7)); the reference server
        // sends NO self SCUnitState here — its SCUnitState (0x97) frames are all nearby NPCs, emitted only after
        // the client's NotifyInGame. Echoing the player's own 0x97 (name + full state) at spawn is a v1.2 leftover.
        // Connection.SendPacket(new SCUnitStatePacket(Connection.ActiveChar));

        Connection.ActiveChar.PushSubscriber(
            TimeManager.Instance.Subscribe(Connection, new TimeOfDayObserver(Connection.ActiveChar))
        );

        Logger.Info("CSSpawnCharacterPacket");
    }
}

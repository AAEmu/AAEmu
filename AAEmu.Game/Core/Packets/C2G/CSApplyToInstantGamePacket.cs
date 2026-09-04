using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSApplyToInstantGamePacket() : GamePacket(CSOffsets.CSApplyToInstantGamePacket, 1)
{
    private uint _type;
    private byte _skipAvailable;

    public override void Read(PacketStream stream)
    {
        _type = stream.ReadUInt32();
        _skipAvailable = stream.ReadByte();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        // Battlefield ids win when both namespaces collide.
        if (BattlefieldGameData.Instance.GetBattlefield(_type) is not null)
        {
            InstantGameManager.Instance.ApplyToBattlefield(
                _type,
                (Models.Game.InstantGame.Static.InstantCorps)_skipAvailable,
                character);
            return;
        }

        if (IndunMatchmakingManager.Instance.TryApply(_type, character))
            return;

        Logger.Warn("CSApplyToInstantGame: unknown type={0} skipAvailable={1} char={2}",
            _type, _skipAvailable, character.Name);
        character.SendPacket(new G2C.SCAppliedToInstantGamePacket(_type, errorMessageId: 1));
    }
}

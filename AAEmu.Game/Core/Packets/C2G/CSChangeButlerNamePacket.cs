using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Changes the active character's bound farmhand name.</summary>
/// <remarks>
/// which passes each field name alongside the value:
/// string name
/// </remarks>
public class CSChangeButlerNamePacket() : GamePacket(CSOffsets.CSChangeButlerNamePacket, 1)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string Name { get; private set; }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes < sizeof(short))
            throw new InvalidDataException("Missing farmhand-name string length.");

        var encodedLength = stream.ReadInt16();
        if (encodedLength < 0 || encodedLength > ButlerRenameService.MaximumUtf8ByteCount ||
            stream.LeftBytes != encodedLength)
            throw new InvalidDataException(
                $"Invalid farmhand-name body length {encodedLength}; {stream.LeftBytes} encoded bytes remain.");

        try
        {
            Name = StrictUtf8.GetString(stream.ReadBytes(encodedLength));
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Farmhand name is not valid UTF-8.", ex);
        }
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var service = SingletonContainer.ServiceProvider?.GetService<IButlerRenameService>();
        if (service == null)
        {
            SendFailure(character, ErrorMessageType.InternalError);
            return;
        }

        var result = service.Rename(character, Name);
        if (!result.Success)
        {
            SendFailure(character, result.Failure switch
            {
                ButlerRenameFailure.InvalidName => ErrorMessageType.CreateInvalidName,
                ButlerRenameFailure.NotBound => ErrorMessageType.NoInteractionAvailable,
                _ => ErrorMessageType.InternalError
            });
            return;
        }

        // The service queues the successful 0x10 update while the persisted state is still locked,
        // preserving request-order publication with other farmhand operations.
    }

    private static void SendFailure(Character character, ErrorMessageType error) =>
        character.SendPacket(CreateUpdate(error, 0, string.Empty));

    private static SCButlerInfoUpdatedPacket CreateUpdate(
        ErrorMessageType error,
        short flags,
        string name) =>
        new(
            (ushort)error,
            flags,
            false,
            false,
            Array.Empty<ButlerActabilityWire>(),
            new Dictionary<sbyte, ulong>(),
            0,
            0,
            0,
            name,
            new Dictionary<uint, uint>());
}

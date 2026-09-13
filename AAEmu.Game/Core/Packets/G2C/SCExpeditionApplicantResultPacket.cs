using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C;
public sealed class SCExpeditionApplicantResultPacket(bool result, string name) : GamePacket(SCOffsets.SCExpeditionApplicantResultPacket, 1)
{ public override PacketStream Write(PacketStream stream) { stream.Write(result); stream.Write(name); return stream; } }

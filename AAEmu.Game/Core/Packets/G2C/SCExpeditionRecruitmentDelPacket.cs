using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
namespace AAEmu.Game.Core.Packets.G2C;
public sealed class SCExpeditionRecruitmentDelPacket() : GamePacket(SCOffsets.SCExpeditionRecruitmentDelPacket, 1)
{ public override PacketStream Write(PacketStream stream) => stream; }

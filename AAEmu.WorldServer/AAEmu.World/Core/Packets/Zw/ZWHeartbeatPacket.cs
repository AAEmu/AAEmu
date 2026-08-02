namespace AAEmu.World.Core.Packets.Zw;

public static class ZWHeartbeatPacket
{
    public static void Read(byte[] body)
    {
        // Heartbeat body is empty or minimal; nothing to parse in Phase 1.
    }
}

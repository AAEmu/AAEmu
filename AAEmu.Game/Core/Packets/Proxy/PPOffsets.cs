namespace AAEmu.Game.Core.Packets.Proxy
{
    public static class PPOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        public const ushort ChangeStatePacket = 0x000;
        public const ushort FinishStatePacket = 0x001;
        public const ushort FlushMsgsPacket = 0x002;
        public const ushort UpdatePhysicsTimePacket = 0x004;
        public const ushort BeginUpdateObjPacket = 0x005;
        public const ushort EndUpdateObjPacket = 0x006;
        public const ushort BeginBindObjPacket = 0x007;
        public const ushort EndBindObjPacket = 0x008;
        public const ushort UnbindPredictedObjPacket = 0x009;
        public const ushort RemoveStaticObjPacket = 0x00a;
        public const ushort VoiceDataPacket = 0x00b;
        public const ushort UpdateAspectPacket = 0x00c;
        public const ushort SetAspectProfilePacket = 0x00d;
        public const ushort PartialAspectPacket = 0x00e;
        public const ushort SetGameTypePacket = 0x00f;
        public const ushort ChangeCVarPacket = 0x010;
        public const ushort EntityClassRegistrationPacket = 0x011;
        public const ushort PingPacket = 0x012;
        public const ushort PongPacket = 0x013;
        public const ushort PacketSeqChange = 0x014;
        public const ushort FastPingPacket = 0x015;
        public const ushort FastPongPacket = 0x016;
    }
}

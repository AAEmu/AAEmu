namespace AAEmu.Login.Core.Packets.L2C
{
    public static class LCOffsets
    {
        // All opcodes here are updated for version client_12_r208022
        public const ushort ACJoinResponsePacket = 0x000;
        public const ushort CARequestAuthPacket = 0x001;
        public const ushort ACChallengePacket = 0x002;
        public const ushort ACAuthResponsePacket = 0x003;
        public const ushort ACChallenge2Packet = 0x004;
        public const ushort ACEnterOtpPacket = 0x005;
        public const ushort ACShowArsPacket = 0x006;
        public const ushort ACEnterPcCertPacket = 0x007;
        public const ushort ACWorldListPacket = 0x008;
        public const ushort ACWorldQueuePacket = 0x009;
        public const ushort ACWorldCookiePacket = 0x00a;
        public const ushort ACEnterWorldDeniedPacket = 0x00b;
        public const ushort ACLoginDeniedPacket = 0x00c;
        public const ushort ACAccountWarnedPacket = 0x00d;
    }
}

namespace AAEmu.Login.Core.Packets.C2L;

public static class CLOffsets
{
    // ClientToAuth opcodes — verified against client version v10.0.3_r575.
    // Note: the regional auth variants (Tencent/GameOn/Trion/MailRu) no longer exist in this client;
    // their old opcodes were repurposed and everything shifted down.
    public const ushort CARequestAuthPacket = 0x001;
    public const ushort CARequestWebAuthPacket = 0x002;
    public const ushort CAChallengeResponsePacket = 0x003;
    public const ushort CAChallengeResponse2Packet = 0x004;
    public const ushort CAOtpNumberPacket = 0x005;
    public const ushort CATestArsPacket = 0x006;
    public const ushort CAPcCertNumberPacket = 0x007;
    public const ushort CAListWorldPacket = 0x008;
    public const ushort CAEnterWorldPacket = 0x009;
    public const ushort CACancelEnterWorldPacket = 0x00a;
    public const ushort CARequestReconnectPacket = 0x00b;
    public const ushort CARequestAuthPWDPacket = 0x012;
    public const ushort CARequestVarifySNPacket = 0x013;
    public const ushort CAPongPacket = 0x014;
}
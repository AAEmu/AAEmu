namespace AAEmu.Login.Network.Message
{
    public enum LoginMessageOpcode : ushort
    {
        ClientRequestAuth = 1,
        ClientRequestAuthTencent = 2,
        ClientRequestAuthGameOn = 3,
        ClientRequestAuthTrion = 4,
        ClientRequestAuthMailRu = 5,
        ClientChallengeResponse = 6,
        ClientChallengeResponse2 = 7,
        ClientOtpNumber = 8,
        ClientPcCertNumber = 0xa,
        ClientListWorld = 0xb,
        ClientEnterWorld = 0xc,
        ClientCancelEnterWorld = 0xd,
        ClientRequestReconnect = 0xe,

        // TODO fix opcodes
        ServerJoinResponse = 0,
        ServerChallenge = 2, 
        ServerAuthResponse = 0x03,
        ServerChallenge2 = 4,
        ServerEnterOtp = 5,
        ServerShowArs = 6,
        ServerEnterPcCert = 7,
        ServerWorldList = 8,
        ServerWorldQueue = 9,
        ServerWorldCookie = 0x0A,
        ServerEnterWorldDenied = 0x0B,
        ServerLoginDenied = 0x0C,
        ServerAccountWarned = 0x0D,
    }
}

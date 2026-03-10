namespace Protocol
{
    public enum PacketId : ushort
    {
        // 시스템 패킷 (1~99)
        Connect = 1,
        Disconnect = 2,
        Heartbeat = 3,

        // 인증 관련 패킷 (100~199)
        LoginRequest = 100,
        LoginResponse = 101,

        // 채팅 관련 패킷 (200~299)
        ChatMessage = 200,
        WhisperMessage = 201,

        // 게임 상태 패킷 (300~399)
        PlayerMove = 300,
        PlayerAction = 301,
        SpawnObject = 302,

        // 기타 패킷 (400~)
        // ...
    }
}

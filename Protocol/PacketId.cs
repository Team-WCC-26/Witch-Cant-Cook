namespace Protocol;

/// <summary>
/// 짝수 -> 클라 패킷
/// 홀수 -> 서버 패킷
/// </summary>
public enum PacketId : ushort // 명명 규칙 request/response로 할지 C_/S_로 할지 정해야할듯
{
    // 시스템 패킷 (0~99)
    C_Connect = 2,
    
    C_Disconnect = 4,
    S_Disconnect = 5,

    C_Heartbeat = 6,
    S_Heartbeat = 7,

    C_Ping = 8,
    S_Pong = 9,

    C_PingResult = 10,

    // 인증 관련 패킷 (100~199)
    C_LoginRequest = 100,
    S_LoginResponse = 101,

    // 룸 관련 패킷 (200~299)
    C_CreateRoom = 200,
    S_CreateRoom = 201, // 생성 즉시 방 참가해야 해서 S_JoinRoom으로 통합해야 할수도

    C_GetRoom = 202,
    S_GetRoom = 203,

    C_JoinRoom = 204,
    S_JoinRoom = 205,

    S_PlayerEnter = 207,

    C_LeaveRoom = 208,
    S_PlayerLeave = 209,

    C_ChatMessage = 210,
    S_ChatMessage = 211,

    S_Notification = 213,

    C_InteractLobbyDoor = 250,
    S_OpenLobbyDoor = 251,

    C_InteractKitchenDoor = 252,
    S_OpenKitchenDoor = 253,

    // 게임 상태 패킷 (300~399) // TODO => 클라쪽 데이터 형식 요청 후 작성
    S_WorldState = 301,

    C_PlayerMove = 302,

    C_EntityRegister = 330,
    S_EntityRegister = 331,

    C_EntityDestroy = 332,
    S_EntityDestroy = 333,

    C_EntityPickup = 334,
    S_EntityPickup = 335,

    C_EntityThrow = 336,
    S_EntityThrow = 337,

    C_IngredientSpawn = 340,
    S_IngredientSpawn = 341,

    C_IngredientCut = 342,

    C_IngredientGrill = 344,

    C_IngredientBoil = 346,

    C_IngredientRoast = 348,

    C_IngredientCancelGrill = 350,

    C_IngredientCombine = 352,
    S_IngredientCombine = 353,

    C_IngredientPut = 354,
    S_IngredientPut = 355,

    C_ToolRegister = 370,
    S_ToolRegister = 371,

    C_ToolSpawn = 372,
    S_ToolSpawn = 373,

    S_AddDish = 401,

    C_ServeDish = 402,
    S_ServeDish = 403,

    // 기타 패킷 (00~)
    // ...
}

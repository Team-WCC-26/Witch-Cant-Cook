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

    C_SessionHello = 14,
    S_SessionTicket = 15,

    C_ReconnectRequest = 16,
    S_ReconnectResult = 17,

    C_SnapshotApplied = 18,
    S_ReconnectSnapshot = 19,

    C_PingResult = 10,

    C_SetLatency = 12, // 테스트용 지연 시뮬레이터

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

    S_PlayerRespawn = 215,

    C_InteractDoor = 250,

    C_StopInteractDoor = 252,
    S_OpenDoor = 253,

    // 게임 상태 패킷 (300~399) // TODO => 클라쪽 데이터 형식 요청 후 작성
    S_WorldState = 301,

    C_PlayerMove = 302,

    C_PlayerDamage = 304,

    C_StageSet = 310,
    S_StageSet = 311,

    C_StageStart = 312,
    S_StageStart = 313,

    C_StageStop = 314,
    S_StageStop = 315,

    //C_EntityRegister = 330,
    //S_EntityRegister = 331,

    C_EntityDestroy = 330,
    S_EntityDestroy = 331,

    //C_EntityPickup = 334,
    //S_EntityPickup = 335,

    C_EntityInsert = 332,

    C_EntityInteract = 334,

    C_EntityExit = 336,

    C_EntityThrow = 338,
    S_EntityThrow = 339,

    //C_EntityCombine = 338,
    //S_EntityCombine = 339,

    //C_EntityPut = 340,
    //S_EntityPut = 341,

    S_IngraedientConveySpawn = 341,

    C_IngredientSpawn = 342,
    S_IngredientSpawn = 343,

    S_IngredientCombine = 345,

    C_IngredientCollision = 350,
    S_IngredientCorn = 351,
    S_IngredientSquid = 353,
    S_IngredientTomato = 355,
    S_IngredientSalmon = 357,
    C_IngredientPosition = 358,
    S_IngredientActivation = 359,

    C_IngredientDistanceResponse = 360,
    S_IngredientDistanceRequest = 361,

    //C_CookStart = 344,
    S_CookStart = 347,

    //C_CookCancel = 346,
    S_CookPause = 349,

    C_ToolRegister = 370,
    S_ToolRegister = 371,

    C_ToolSpawn = 372,
    S_ToolSpawn = 373,

    S_DishState = 401,

    C_ServeDish = 402,
    S_ServeDish = 403,

    //C_ClearDish = 404,
    //S_ClearDish = 405,

    S_RoomMoney = 406,

    S_RoomPendingMoney = 408,

    // Pot
    C_PotForceEject = 410,
    S_PotEject = 411,

    C_ToolPlayerEnter = 412,
    S_ToolPlayerEnter = 413,

    C_ToolPlayerExit = 414,
    S_ToolPlayerExit = 415,

    S_PotCookComplete = 417,

    // Pan
    C_PanStoveExit = 418,
    S_PanStoveExit = 419,

    // JumpPad
    C_JumpPadPlayerEnter = 420,

    C_JumpPadPlayerExit = 422,
    S_JumpPadEvent = 423,

    // Pan
    S_PanEject = 425,

    // Dish
    S_DishEject = 427,

    // TrashCan
    S_TrashCanEject = 429,

    // 기타 패킷 (00~)
    // ...
}

using Protocol;
using Server;
using System;
using UnityEngine;

public class StageManager : Singleton<StageManager>
{
    // 열어야 하는 문 정보
    [SerializeField] private Door lobbyDoor;
    [SerializeField] private Door kitchenDoor;

    private PhaseBase currentPhase = null;
    public PhaseBase CurrentPhase => currentPhase;

    [SerializeField]
    private StageConfig config;
    private int currentStageIndex = 0;

    public StageData CurrentStageData => config.allStages[currentStageIndex];

    // 페이즈들 저장
    private PrepPhase prepPhase;
    private CookingPhase cookingPhase;
    private JudgePhase judgePhase;

    private bool isGameStarted = false; // 게임 시작 여부 체크용 (선택)

    public void StartPrep() => ChangePhase(prepPhase);
    public void StartCooking() => ChangePhase(cookingPhase);
    public void StartJudging() => ChangePhase(judgePhase);

    #region Stage Actions
    public static event Action<DoorId> DoorOpened;
    #endregion

    void Start()
    {
        ServerManager.Instance.RegisterHandler(
            PacketId.S_OpenDoor,
            data => OnOpenDoor(data));

        //config = Resources.Load<StageConfig>("StageConfig");

        // 페이즈 초기화
        prepPhase = new PrepPhase(this);
        cookingPhase = new CookingPhase(this);
        judgePhase = new JudgePhase(this);

        // 문 관리
        lobbyDoor = GameObject.Find("LobbyDoor").GetComponent<Door>();
        kitchenDoor = GameObject.Find("KitchenDoor").GetComponent<Door>();

        lobbyDoor.OpenImmediate();
        kitchenDoor.CloseImmediate();
    }

    void Update()
    {
        currentPhase?.OnUpdate();
    }

    private void OnDestroy()
    {
        //ServerManager.Instance.UnRegisterHandler(PacketId.S_OpenDoor);
    }


    public void ChangePhase(PhaseBase newPhase)
    {
        currentPhase?.OnExit();
        currentPhase = newPhase;
        currentPhase.OnEnter();
    }

    public void FinishStage()
    {
        // 게임 오버 조건 체크

        // 게임 클리어 조건 체크

        // 아무 조건에도 걸리지 않으면 다음 라운드로 이동
        NextRound();
    }

    private void NextRound()
    {
        currentStageIndex++;
        ChangePhase(prepPhase);
    }

    private void GameClear()
    {
        PlayEndingScene();
    }

    private void GameOver()
    {
    }

    private void PlayEndingScene()
    {
        // 엔딩 연출 호출
    }

    public void StartGame()
    {
        if (isGameStarted) return; // 이미 시작했다면 중복 실행 방지

        isGameStarted = true;

        ChangePhase(prepPhase);
    }

    /// <summary>
    /// 테스트용 > 현재 페이즈를 건너뛰고 다음 페이즈로 이동
    /// </summary>
    public void SkipToNext()
    {
        if (currentPhase is PrepPhase) StartCooking();
        else if (currentPhase is CookingPhase) StartJudging();
        else if (currentPhase is JudgePhase) FinishStage();
    }

    private void OnOpenDoor(ReadOnlyMemory<byte> data)
    {
        OpenDoorPacket packet = PacketSerializer.Deserialize<OpenDoorPacket>(data);

        switch (packet.DoorId)
        {
            case DoorId.Lobby:
                lobbyDoor.Open();
                break;

            case DoorId.Kitchen:
                lobbyDoor.Close();
                kitchenDoor.Open();
                //StartCooking();
                break;
        }
        DoorOpened?.Invoke(packet.DoorId);
    }
}

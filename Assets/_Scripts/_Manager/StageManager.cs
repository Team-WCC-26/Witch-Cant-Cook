using Protocol;
using Server;
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class StageManager : Singleton<StageManager>
{
    // 열어야 하는 문 정보
    [SerializeField] private Door lobbyDoor;
    [SerializeField] private Door kitchenDoor;
    // 폐기된 내용, 삭제 필요 -----------------------------
    public float happiness = 0f; // 0.0 ~ 1.0 (100%)
    public int lives = 3;

    // 판정 조건 수치
    private readonly float HAPPINESS_STANDARD = 0.7f;
    private readonly int TARGET_STAGE_COUNT = 7;
    // --------------------------------------------------

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

    void Start()
    {
        ServerManager.Instance.RegisterHandler(PacketId.S_OpenDoor,OnOpenDoor);
        //config = Resources.Load<StageConfig>("StageConfig");

        // 페이즈 초기화
        prepPhase = new PrepPhase(this);
        cookingPhase = new CookingPhase(this);
        judgePhase = new JudgePhase(this);

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
        ServerManager.Instance.UnRegisterHandler(PacketId.S_OpenDoor);
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
        if (happiness < HAPPINESS_STANDARD || lives <= 0)
        {
            GameOver();
            return;
        }

        // 게임 클리어 조건 체크
        if (currentStageIndex >= TARGET_STAGE_COUNT - 1)
        {
            GameClear();
            PlayEndingScene();
            return;
        }

        // 아무 조건에도 걸리지 않으면 다음 라운드로 이동
        NextRound();
    }

    private void NextRound()
    {
        currentStageIndex++;
        Debug.Log($"{currentStageIndex + 1} 라운드로 이동합니다.");
        ChangePhase(prepPhase);
    }

    private void GameClear()
    {
        Debug.Log("게임 클리어");
        PlayEndingScene();
    }

    private void GameOver()
    {
        Debug.Log("게임 오버");
    }

    private void PlayEndingScene()
    {
        // 엔딩 연출 호출
    }

    public void StartGame()
    {
        if (isGameStarted) return; // 이미 시작했다면 중복 실행 방지

        Debug.Log("<color=green>▶ 게임 시작!</color>");
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
        OpenDoorPacket packet =
            PacketSerializer.Deserialize<OpenDoorPacket>(data);

        switch (packet.DoorId)
        {
            case DoorId.Lobby:
                lobbyDoor.Open();
                break;

            case DoorId.Kitchen:
                lobbyDoor.Close();
                kitchenDoor.Open();
                StartCooking();
                break;
        }
    }
}

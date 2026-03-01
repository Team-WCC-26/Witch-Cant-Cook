using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class StageManager : Singleton<StageManager>
{
    // 임시 > 디테일화 필요
    public float happiness = 0f; // 0.0 ~ 1.0 (100%)
    public int lives = 3;


    private PhaseBase currentPhase;
    private StageConfig config;
    private int currentStageIndex = 0;

    public StageData CurrentStageData => config.allStages[currentStageIndex];

    // 페이즈들 저장
    private PrepPhase prepPhase;
    private CookingPhase cookingPhase;
    private JudgePhase judgePhase;

    void Start()
    {
        config = Resources.Load<StageConfig>("StageConfig");
        // 페이즈 초기화
        prepPhase = new PrepPhase(this);
        cookingPhase = new CookingPhase(this);
        judgePhase = new JudgePhase(this);

        // 첫 라운드 시작: 준비 페이즈로
        ChangePhase(prepPhase);
    }

    void Update()
    {
        currentPhase?.OnUpdate();
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
        if (happiness < 0.7f || lives <= 0)
        {
            GameOver();
            return;
        }

        // 게임 클리어 조건 체크
        const int TARGET_STAGE_COUNT = 7;

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

}

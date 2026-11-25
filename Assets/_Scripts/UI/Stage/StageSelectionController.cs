using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class StageSelectionController : MonoBehaviour
{
    [SerializeField] private StageSelectionWorldUI view;
    private DataManager data;
    private StageSelectionSession session;
    private StagePageIndex stages;
    private bool initialized;
    private bool lastConnected;

    public int SelectedStageId => session != null ? session.SelectedStageId : 0;

    public void Configure(StageSelectionWorldUI boardView) => view = boardView;

    private void OnEnable()
    {
        if (view == null) view = GetComponent<StageSelectionWorldUI>();
        initialized = false;
        TrySubscribe();
    }

    private void Update()
    {
        if (session == null) TrySubscribe();
        if (data == null) data = FindFirstObjectByType<DataManager>();
        if (!initialized && data != null && data.IsDataLoaded) RebuildStageData();

        bool connected = session != null && session.IsRoomReady;
        if (connected != lastConnected)
        {
            lastConnected = connected;
            RefreshStage();
        }
    }

    private void TrySubscribe()
    {
        if (session != null) return;
        session = StageSelectionSession.Instance;
        session.Changed += RefreshStage;
        RefreshStage();
    }

    [ContextMenu("Refresh Server Stage Data (Play Mode)")]
    public void RebuildStageData()
    {
        if (!Application.isPlaying || data == null || !data.IsDataLoaded) return;
        var stageRows = data.GetStage().GetAll();
        var stageNumbers = new HashSet<int>();
        foreach (Stage row in stageRows)
            if (row != null && (row.stageNum <= 0 || !stageNumbers.Add(row.stageNum)))
                Debug.LogWarning($"Stage: invalid or duplicate stageNum {row.stageNum} (id {row.id}); row ignored.", this);

        stages = new StagePageIndex(stageRows, data.GetIngredientGroup().GetAll());
        initialized = true;
        foreach (int stageId in stages.StageIds)
            if (stages.GetIngredientIds(stageId).Count > 6)
                Debug.LogWarning($"Stage {stageId} has more than 6 unique ingredients; only the first 6 are displayed.", this);
        RefreshStage();
    }

    private void RefreshStage()
    {
        if (view == null) return;
        if (session == null || !session.IsRoomReady)
        {
            view.Clear("방에 입장하면 스테이지 정보가 표시됩니다.");
            return;
        }
        if (!session.HasStageSelection)
        {
            view.Clear("서버 스테이지 정보 대기 중");
            return;
        }
        if (!initialized)
        {
            view.Clear("스테이지 데이터 로딩 중");
            return;
        }
        if (!stages.TryGetStage(session.SelectedStageId, out Stage stage))
        {
            view.Clear($"스테이지 {session.SelectedStageId}: Stage 데이터가 없습니다.");
            return;
        }
        view.Show(stage, stages, data);
        view.SetStatus(session.IsStageRunning ? "스테이지 진행 중" : "");
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= RefreshStage;
        session = null;
    }
}

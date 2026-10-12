using System;
using UnityEngine;

[DisallowMultipleComponent]
public class StageSelectionController : MonoBehaviour
{
    [SerializeField] private StageSelectionWorldUI view;
    [SerializeField, Min(.1f)] private float collisionCooldown = .35f;
    [SerializeField, Min(1f)] private float responseTimeout = 5f;

    private DataManager data;
    private StageSelectionSession session;
    private StagePageIndex pages;

    private int selectedStageId = 1;  // 선택한 스테이지 ID
    private bool stageRunning; // 스테이지 진행 중 여부
    private int pendingStageId; // 서버 응답 대기 중인 스테이지 ID
    private float pendingUntil; // 서버 응답 대기 시간
    private float nextRequestAt; // 다음 요청 가능 시간
    private bool initialized; // 데이터 초기화 여부
    private bool lastConnected; // 마지막 연결 상태

    public int SelectedStageId => selectedStageId;

    public void Configure(StageSelectionWorldUI boardView)
    {
        view = boardView;
    }

    private void OnEnable()
    {
        if (view == null) view = GetComponent<StageSelectionWorldUI>();
        if (view != null) view.Clear("스테이지 데이터 로딩 중");
        initialized = false;

        TrySubscribe();
    }

    private void Update()
    {
        if (session == null) TrySubscribe();
        if (data == null) data = FindFirstObjectByType<DataManager>();
        if (!initialized && data != null && data.IsDataLoaded) RebuildPages();
        bool connected = session != null && session.IsRoomReady;
        if (connected != lastConnected) { lastConnected = connected; RefreshNavigation(); }
        if (pendingStageId != 0 && Time.unscaledTime >= pendingUntil)
        {
            pendingStageId = 0;
            RefreshNavigation("서버 응답이 없습니다. 다시 충돌해 주세요.");
        }
    }

    private void TrySubscribe()
    {
        session = StageSelectionSession.Instance;
        session.Changed += OnSelectionChanged;
        OnSelectionChanged();
    }

    [ContextMenu("Refresh Stage Pages (Play Mode)")]
    public void RebuildPages()
    {
        if (!Application.isPlaying || data == null || !data.IsDataLoaded) return;
        var stageRows = data.GetStage().GetAll();
        var stageNumbers = new System.Collections.Generic.HashSet<int>();
        foreach (Stage row in stageRows)
        {
            if (row == null) continue;
            if (row.stageNum <= 0 || !stageNumbers.Add(row.stageNum))
                Debug.LogWarning($"Stage: invalid or duplicate stageNum {row.stageNum} (id {row.id}); row ignored.", this);
        }
        pages = new StagePageIndex(stageRows, data.GetIngredientGroup().GetAll());
        initialized = true;
        foreach (int stageId in pages.StageIds)
            if (pages.GetIngredientIds(stageId).Count > 6)
                Debug.LogWarning($"Stage {stageId} has more than 6 unique ingredients; only the first 6 are displayed.", this);
        RefreshPage();
    }

    // Only the owning client sends a collision request; remote player physics must not duplicate it.
    public async void TurnPage(int direction, PlayerBrain player)
    {
        if (direction == 0 || player == null || pages == null || stageRunning || pendingStageId != 0 ||
            Time.unscaledTime < nextRequestAt) return;
        var players = PlayerSpawnManager.Instance;
        if (session == null || !session.IsRoomReady || players == null ||
            string.IsNullOrEmpty(player.PlayerId) || !players.IsMine(player.PlayerId)) return;
        if (!pages.TryGetAdjacentStage(selectedStageId, direction, out int requestedStageId)) return;
        pendingStageId = requestedStageId;
        pendingUntil = Time.unscaledTime + responseTimeout;
        nextRequestAt = Time.unscaledTime + collisionCooldown;
        RefreshNavigation();
        try
        {
            await session.RequestSelection(requestedStageId);
        }
        catch (Exception exception)
        {
            if (pendingStageId == requestedStageId) pendingStageId = 0;
            RefreshNavigation("스테이지 변경 요청을 보내지 못했습니다.");
            Debug.LogException(exception, this);
        }
    }

    private void OnSelectionChanged()
    {
        selectedStageId = session.SelectedStageId;
        stageRunning = session.IsStageRunning;
        // Another player's broadcast may arrive before our own request acknowledgment.
        if (pendingStageId == selectedStageId || stageRunning || !session.IsRoomReady) pendingStageId = 0;
        RefreshPage();
    }

    private void RefreshPage()
    {
        if (view == null || !initialized) return;
        if (pages.StageIds.Count == 0)
        {
            view.Clear("Stage 테이블을 연결해 주세요.");
            return;
        }
        if (!pages.TryGetStage(selectedStageId, out Stage stage))
        {
            view.Clear($"스테이지 {selectedStageId}: Stage 데이터가 없습니다.");
            return;
        }
        view.Show(stage, pages, data);
        RefreshNavigation();
    }

    private void RefreshNavigation(string status = null)
    {
        if (view == null || !initialized || pages.StageIds.Count == 0) return;
        int index = pages.IndexOf(selectedStageId);
        bool connected = session != null && session.IsRoomReady;
        bool canTurn = connected && !stageRunning && pendingStageId == 0 && index >= 0;
        status ??= index < 0 ? $"스테이지 {selectedStageId}: Stage 데이터가 없습니다." :
            stageRunning ? "스테이지 진행 중" : pendingStageId != 0 ? "페이지 변경 중" :
            "";
        view.SetNavigation(canTurn && index > 0, canTurn && index < pages.StageIds.Count - 1, status);
    }

    private void OnDisable()
    {
        if (session != null) session.Changed -= OnSelectionChanged;
        session = null;
        pendingStageId = 0;
    }
}

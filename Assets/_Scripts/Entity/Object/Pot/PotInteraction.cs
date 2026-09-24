using System.Collections;
using System.Collections.Generic;
using Protocol;
using Server;
using UnityEngine;

public enum PotState
{
    Idle,
    Cooking,
    Ejecting
}

public class PotInteraction : MapObjInteraction,
    IPunchReceiver,
    IEntityParentReceiver,
    ICookReceiver,
    IPotNetworkReceiver
{
    #region Inspecter

    [Header("Storage")]
    [SerializeField] private Transform parentRecipe;
    [SerializeField] private Transform parentETC;

    [Header("Cooking")]
    [SerializeField] private InteractionGaugeUI gaugeUI;

    #endregion

    private PotState state = PotState.Idle;
    public PotState State => state;

    // Object storage
    private readonly HashSet<CatchableObj> pendingObjects = new(); // Awaiting server response
    private readonly HashSet<CatchableObj> storedObjects = new(); // Total Input
    private readonly List<CatchableObj> recipeObjects = new(); // During Idle State
    private readonly List<CatchableObj> etcObjects = new(); // During Cooking State
    private readonly List<CatchableObj> postEjectObjects = new(); // During Ejecting
    private CatchableObj cookingBowl; // Cooking Trigger

    // Player entry
    private readonly List<PlayerBrain> trappedPlayers = new();
    private readonly HashSet<string> pendingPlayerEntries = new(); // Awaiting server response

    // Network 
    private ObjectNetworkRouter objectRouter;
    private PotCookCompletePacket pendingCookCompletePacket;
    private float? pendingCookingDuration;

    #region Lifecycle

    // 솥 패킷에서 오브젝트를 찾을 라우터 연결
    public override void InitializeRouter(MapObjNetworkRouter router)
    {
        base.InitializeRouter(router);
        objectRouter = router != null ? router.GetComponent<ObjectNetworkRouter>() : null;
    }

    // 솥 활성화 초기화
    private void OnEnable()
    {
        ResetPotData();
    }

    // 솥 보관물 반환 및 초기화
    private void OnDisable()
    {
        StopPotCoroutines();
        ClearObjects();
        ResetPotData();
    }

    // 솥 데이터 초기화
    private void ResetPotData()
    {
        state = PotState.Idle;

        // Object storage
        pendingObjects.Clear();
        storedObjects.Clear();
        recipeObjects.Clear();
        etcObjects.Clear();
        postEjectObjects.Clear();
        cookingBowl = null;

        // Player entry
        trappedPlayers.Clear();
        pendingPlayerEntries.Clear();

        // Network
        pendingCookCompletePacket = null;
        pendingCookingDuration = null;

        // Ejection
        ejectCoroutine = null;
        pendingEjectPackets.Clear();
        activeEjectPlayers.Clear();
        activeEjectObjects.Clear();
        ejectRandom = null;

        gaugeUI?.Hide();
    }

    // 실행 중 솥 코루틴 중단
    private void StopPotCoroutines()
    {
        if (ejectCoroutine != null) StopCoroutine(ejectCoroutine);

        ejectCoroutine = null;
    }

    // 보관 오브젝트 Pool 반환
    private void ClearObjects()
    {
        if (ObjectPoolManager.Instance == null) return;

        foreach (CatchableObj catchable in storedObjects)
        {
            if (catchable == null) continue;
            ObjectPoolManager.Instance.Push(catchable.gameObject);
        }
    }

    // 진입 대상 서버 요청
    private void OnTriggerEnter(Collider other)
    {
        if (TryGetPlayer(other, out PlayerBrain player))
        {
            RequestPlayerEntry(player);
            return;
        }

        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null) return;
        if (storedObjects.Contains(catchable) || !pendingObjects.Add(catchable)) return;

        RequestObjEntry(catchable);
    }

    #endregion

    #region Server

    // 솥 진입 패킷 전송
    private void RequestObjEntry(CatchableObj catchable)
    {
        if (catchable == null) return;
        if (NetworkId == 0 || catchable.NetworkId == 0 || ServerManager.Instance == null)
        {
            pendingObjects.Remove(catchable);
            return;
        }

        EntityInsertPacket packet = new()
        {
            SubjectEntityId = catchable.NetworkId,
            TargetEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 솥 진입 결과 수신
    public void ReceiveObjEntry(CatchableObj catchable, PotState entryState)
    {
        if (catchable == null) return;

        pendingObjects.Remove(catchable);
        if (storedObjects.Contains(catchable)) return;

        if (entryState == PotState.Idle)
            HandleIdleEntry(catchable);
        else
        {
            StoreETC(catchable);
            if (entryState == PotState.Ejecting)
                postEjectObjects.Add(catchable);
        }
    }

    // 솥 조리 시작 결과 수신
    public void ReceiveCookStarted(CatchableObj bowl, float duration)
    {
        if (state != PotState.Idle) return;
        if (bowl == null || bowl != cookingBowl) return;
        if (recipeObjects.Count == 0) return;

        ChangeState(PotState.Cooking);
        gaugeUI?.StartFill(duration);
    }

    // 강제 배출 패킷 전송
    private void RequestForceEject()
    {
        if (NetworkId == 0 || ServerManager.Instance == null) return;

        PotForceEjectPacket packet = new()
        {
            PotEntityId = NetworkId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 재료와 그릇 수신 후 대기 중인 조리 시작
    private void TryStartCooking()
    {
        if (!pendingCookingDuration.HasValue) return;
        if (cookingBowl == null || recipeObjects.Count == 0) return;

        float duration = pendingCookingDuration.Value;
        pendingCookingDuration = null;
        ReceiveCookStarted(cookingBowl, duration);
    }

    // 서버가 소비한 조리 재료를 솥 보관 목록에서 제거
    private void RemoveConsumedObj()
    {
        CatchableObj[] consumedObjects = recipeObjects.ToArray();
        recipeObjects.Clear();

        foreach (CatchableObj catchable in consumedObjects)
        {
            if (catchable == null) continue;

            pendingObjects.Remove(catchable);
            storedObjects.Remove(catchable);
            postEjectObjects.Remove(catchable);
        }
    }

    #endregion

    #region Object Storage

    // Idle 상태 진입 오브젝트 분류
    private void HandleIdleEntry(CatchableObj catchable)
    {
        if (catchable.TryGetComponent(out IngredientReaction _))
        {
            StoreRecipe(catchable);
            return;
        }

        if (catchable.TryGetComponent(out PlateInteraction plate) && plate.IsEmpty)
        {
            if (recipeObjects.Count == 0 && !pendingCookingDuration.HasValue)
            {
                StoreETC(catchable);
                return;
            }

            StoreCookingBowl(catchable);
            return;
        }

        StoreETC(catchable);
    }

    // 조리 대상 재료 보관
    private void StoreRecipe(CatchableObj catchable)
    {
        if (!TryStoreObject(catchable, parentRecipe)) return;

        recipeObjects.Add(catchable);
    }

    // 조리 시작 그릇 보관
    private void StoreCookingBowl(CatchableObj bowl)
    {
        if (!TryStoreObject(bowl, parentRecipe)) return;
        cookingBowl = bowl;
    }

    // 배출 대상 오브젝트 보관
    private void StoreETC(CatchableObj catchable)
    {
        if (!TryStoreObject(catchable, parentETC)) return;

        etcObjects.Add(catchable);
    }

    // 오브젝트 비활성 보관
    private bool TryStoreObject(CatchableObj catchable, Transform storageParent)
    {
        if (storageParent == null)
        {
            Debug.LogError("Pot storage parent is not assigned.");
            pendingObjects.Remove(catchable);
            return false;
        }

        if (!storedObjects.Add(catchable)) return false;

        catchable.ChangePickState(false);
        catchable.SetPhysicsState(false);
        catchable.transform.SetParent(storageParent, false);
        catchable.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        catchable.gameObject.SetActive(false);
        return true;
    }

    #endregion

    #region Ejection

    [Header("Ejection")]
    [SerializeField] private Transform ejectPoint;
    [SerializeField, Min(0f)] private float ejectInterval = 0.15f;
    [SerializeField] private Vector3 ejectBaseDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    [SerializeField, Min(0f)] private float ejectForce = 5f;
    [SerializeField, Min(0f)] private float bowlEjectForce = 4f;
    [SerializeField, Min(0f)] private float playerEjectForce = 8f;
    [SerializeField, Min(0.1f)] private float ejectGizmoLength = 2f;

    private Coroutine ejectCoroutine;
    private readonly Queue<PotEjectPacket> pendingEjectPackets = new();
    private readonly Queue<PlayerBrain> activeEjectPlayers = new();
    private readonly Queue<CatchableObj> activeEjectObjects = new();
    private System.Random ejectRandom;

    // 서버 목록 기준 배출 큐 생성
    private void BeginPacketEjection(PotEjectPacket packet)
    {
        activeEjectPlayers.Clear();
        activeEjectObjects.Clear();

        foreach (string playerId in packet.PlayerIds)
        {
            PlayerBrain player = null;
            PlayerSpawnManager.Instance?.TryGetPlayer(playerId, out player);
            activeEjectPlayers.Enqueue(player);
        }

        foreach (long entityId in packet.EntityIds)
        {
            CatchableObj catchable = null;
            objectRouter?.TryGet(entityId, out catchable);
            activeEjectObjects.Enqueue(catchable);
        }

        ejectRandom = new System.Random(packet.EjectSeed);
        pendingCookingDuration = null;
        gaugeUI?.Hide();
        ChangeState(PotState.Ejecting);
        ejectCoroutine = StartCoroutine(EjectRoutine());
    }

    // 서버 배출 순서대로 순차 처리
    private IEnumerator EjectRoutine()
    {
        while (activeEjectPlayers.Count > 0 || activeEjectObjects.Count > 0)
        {
            Vector3 direction = GetEjectDirection();

            if (activeEjectPlayers.Count > 0)
            {
                EjectPlayer(activeEjectPlayers.Dequeue(), direction);
            }
            else
            {
                CatchableObj next = activeEjectObjects.Dequeue();
                bool isCompletedBowl = pendingCookCompletePacket != null &&
                    next != null &&
                    next.NetworkId == pendingCookCompletePacket.DishEntityId;
                float force = isCompletedBowl ? bowlEjectForce : ejectForce;
                EjectObject(next, force, direction);
            }

            if ((activeEjectPlayers.Count > 0 || activeEjectObjects.Count > 0) && ejectInterval > 0f)
                yield return new WaitForSeconds(ejectInterval);
        }

        ejectCoroutine = null;
        FinishPacketEjection();
    }

    // 보관 오브젝트 물리 배출
    private void EjectObject(CatchableObj catchable, float force, Vector3 direction)
    {
        if (catchable == null) return;

        RemoveStoredObject(catchable);
        pendingObjects.Remove(catchable);
        catchable.transform.SetParent(null, false);
        catchable.transform.SetPositionAndRotation(
            GetEjectPosition(),
            ejectPoint != null ? ejectPoint.rotation : transform.rotation);
        catchable.gameObject.SetActive(true);
        catchable.ChangePickState(true);
        catchable.SetPhysicsState(true);

        if (catchable.Rb == null) return;

        catchable.Rb.linearVelocity = Vector3.zero;
        catchable.Rb.angularVelocity = Vector3.zero;
        catchable.Rb.AddForce(direction * force, ForceMode.Impulse);
    }

    // 배출 기준 위치 조회
    private Vector3 GetEjectPosition()
    {
        return ejectPoint != null ? ejectPoint.position : transform.position;
    }

    // 보관 목록에서 오브젝트 제거
    private void RemoveStoredObject(CatchableObj catchable)
    {
        if (catchable == null) return;

        storedObjects.Remove(catchable);
        recipeObjects.Remove(catchable);
        etcObjects.Remove(catchable);
        postEjectObjects.Remove(catchable);

        if (cookingBowl == catchable)
            cookingBowl = null;
    }

    // 랜덤 편차 적용 배출 방향 계산
    private Vector3 GetEjectDirection()
    {
        float randomYMin = Mathf.Min(ejectRandomRangeYMin, ejectRandomRangeYMax);
        float randomYMax = Mathf.Max(ejectRandomRangeYMin, ejectRandomRangeYMax);
        Vector3 randomOffset = new(
            NextEjectRange(-ejectRandomRangeX, ejectRandomRangeX),
            NextEjectRange(randomYMin, randomYMax),
            NextEjectRange(-ejectRandomRangeZ, ejectRandomRangeZ));

        Vector3 localDirection = ejectBaseDirection + randomOffset;
        if (localDirection.sqrMagnitude < 0.0001f)
            localDirection = ejectBaseDirection.sqrMagnitude >= 0.0001f
                ? ejectBaseDirection
                : Vector3.up;

        return transform.TransformDirection(localDirection).normalized;
    }

    // 배출 전용 난수 범위 계산
    private float NextEjectRange(float min, float max)
    {
        ejectRandom ??= new System.Random(0);
        return min + (float)ejectRandom.NextDouble() * (max - min);
    }

    // 현재 배출 완료 및 신규 진입 보존
    private void FinishPacketEjection()
    {
        CatchableObj[] enteredDuringEjection = postEjectObjects.ToArray();
        postEjectObjects.Clear();
        ChangeState(PotState.Idle);

        foreach (CatchableObj catchable in enteredDuringEjection)
        {
            if (catchable == null || !storedObjects.Contains(catchable)) continue;

            storedObjects.Remove(catchable);
            recipeObjects.Remove(catchable);
            etcObjects.Remove(catchable);

            HandleIdleEntry(catchable);
        }

        TryStartCooking();
        pendingCookCompletePacket = null;

        if (pendingEjectPackets.Count > 0)
            BeginPacketEjection(pendingEjectPackets.Dequeue());
    }

    // 선택 시 배출 기준 방향과 랜덤 범위 표시
    private void OnDrawGizmosSelected()
    {
        Vector3 origin = GetEjectPosition();
        float length = Mathf.Max(0.1f, ejectGizmoLength);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, 0.08f);
        Gizmos.DrawLine(origin, origin + GetWorldEjectDirection(ejectBaseDirection) * length);

        float minY = Mathf.Min(ejectRandomRangeYMin, ejectRandomRangeYMax);
        float maxY = Mathf.Max(ejectRandomRangeYMin, ejectRandomRangeYMax);
        Vector3[] endpoints = new Vector3[8];
        int index = 0;

        for (int y = 0; y < 2; y++)
        {
            for (int x = 0; x < 2; x++)
            {
                for (int z = 0; z < 2; z++)
                {
                    Vector3 offset = new(
                        x == 0 ? -ejectRandomRangeX : ejectRandomRangeX,
                        y == 0 ? minY : maxY,
                        z == 0 ? -ejectRandomRangeZ : ejectRandomRangeZ);
                    endpoints[index++] = origin + GetWorldEjectDirection(ejectBaseDirection + offset) * length;
                }
            }
        }

        Gizmos.color = Color.cyan;
        foreach (Vector3 endpoint in endpoints)
            Gizmos.DrawLine(origin, endpoint);

        DrawEjectRangeEdge(endpoints, 0, 1);
        DrawEjectRangeEdge(endpoints, 0, 2);
        DrawEjectRangeEdge(endpoints, 1, 3);
        DrawEjectRangeEdge(endpoints, 2, 3);
        DrawEjectRangeEdge(endpoints, 4, 5);
        DrawEjectRangeEdge(endpoints, 4, 6);
        DrawEjectRangeEdge(endpoints, 5, 7);
        DrawEjectRangeEdge(endpoints, 6, 7);
        DrawEjectRangeEdge(endpoints, 0, 4);
        DrawEjectRangeEdge(endpoints, 1, 5);
        DrawEjectRangeEdge(endpoints, 2, 6);
        DrawEjectRangeEdge(endpoints, 3, 7);
    }

    // 로컬 배출 방향을 안전한 월드 방향으로 변환
    private Vector3 GetWorldEjectDirection(Vector3 localDirection)
    {
        if (localDirection.sqrMagnitude < 0.0001f)
            localDirection = ejectBaseDirection.sqrMagnitude >= 0.0001f
                ? ejectBaseDirection
                : Vector3.up;

        return transform.TransformDirection(localDirection).normalized;
    }

    // 배출 범위 외곽선 표시
    private static void DrawEjectRangeEdge(Vector3[] endpoints, int from, int to)
    {
        Gizmos.DrawLine(endpoints[from], endpoints[to]);
    }

    // Inspector 디버그 강제 배출
    public void DebugForceEject()
    {
        if (ejectCoroutine != null) return;

        PotEjectPacket packet = new()
        {
            PotEntityId = NetworkId,
            EjectSeed = System.Environment.TickCount,
            EjectDelayMs = 0
        };

        foreach (PlayerBrain player in trappedPlayers)
        {
            if (player != null) packet.PlayerIds.Add(player.PlayerId);
        }

        foreach (CatchableObj catchable in recipeObjects)
        {
            if (catchable != null) packet.EntityIds.Add(catchable.NetworkId);
        }

        if (cookingBowl != null)
            packet.EntityIds.Add(cookingBowl.NetworkId);

        foreach (CatchableObj catchable in etcObjects)
        {
            if (catchable != null && catchable != cookingBowl)
                packet.EntityIds.Add(catchable.NetworkId);
        }

        BeginPacketEjection(packet);
    }

    #endregion

    #region Player

    // 로컬 플레이어 솥 진입 전송
    private void RequestPlayerEntry(PlayerBrain player)
    {
        if (player == null || PlayerSpawnManager.Instance == null) return;
        if (!PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
        if (trappedPlayers.Contains(player)) return;
        if (!pendingPlayerEntries.Add(player.PlayerId)) return;
        if (NetworkId == 0 || ServerManager.Instance == null)
        {
            pendingPlayerEntries.Remove(player.PlayerId);
            return;
        }

        ToolPlayerEnterPacket packet = new()
        {
            ToolEntityId = NetworkId,
            PlayerId = player.PlayerId
        };

        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(packet));
    }

    // 서버 큐에서 꺼낸 플레이어 배출
    private void EjectPlayer(PlayerBrain player, Vector3 direction)
    {
        if (player == null) return;

        trappedPlayers.Remove(player);
        pendingPlayerEntries.Remove(player.PlayerId);
        if (player.Interact?.HeldObj != null)
        {
            CatchableObj heldObject = player.Interact.HeldObj;
            player.Interact.ForceDropHeld();

            if (player.Interact.TryReleaseHeld(heldObject))
                heldObject.OnDrop();
        }

        player.EffectController?.ApplyKnockback(direction * playerEjectForce);
    }

    // 플레이어 부모 컴포넌트 탐색
    private static bool TryGetPlayer(Collider other, out PlayerBrain player)
    {
        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    #endregion

    #region State

    // 솥 상태 변경
    private void ChangeState(PotState nextState)
    {
        if (state == nextState) return;
        state = nextState;
    }

    #endregion

    #region Interface Implementations

    // IEntityParentReceiver: 서버 부모 변경으로 오브젝트 진입 
    public void HandleEntityAdded(CatchableObj entity)
    {
        ReceiveObjEntry(entity, state);
        TryStartCooking();
    }

    // IEntityParentReceiver: 서버 부모 변경으로 오브젝트 이탈 
    public void HandleEntityRemoved(CatchableObj entity)
    {
        pendingObjects.Remove(entity);
        RemoveStoredObject(entity);
    }

    // ICookReceiver: 서버 조리 시작 시간 
    public void HandleCookStart(CookStartPacket packet)
    {
        pendingCookingDuration = Mathf.Max(0f, packet.CookingTimeMs / 1000f);
        TryStartCooking();
    }

    // ICookReceiver: 일반 조리 일시정지 수신(미사용)
    public void HandleCookPause(CookPausePacket packet)
    {
    }

    // ICookReceiver: 일반 조리 완료 수신(미사용)
    public void HandleCookComplete(CookCompletePacket packet)
    {
    }

    // IPotNetworkReceiver: 서버 플레이어 진입 
    public void HandlePotPlayerEnter(ToolPlayerEnterPacket packet)
    {
        pendingPlayerEntries.Remove(packet.PlayerId);
        if (PlayerSpawnManager.Instance == null) return;
        if (!PlayerSpawnManager.Instance.TryGetPlayer(packet.PlayerId, out PlayerBrain player)) return;
        if (!trappedPlayers.Contains(player)) trappedPlayers.Add(player);
    }

    // IPotNetworkReceiver: 서버 배출 패킷 
    public void HandlePotEject(PotEjectPacket packet)
    {
        if (packet == null) return;

        if (ejectCoroutine != null)
        {
            pendingEjectPackets.Enqueue(packet);
            return;
        }

        BeginPacketEjection(packet);
    }

    // IPotNetworkReceiver: 서버 솥 조리 완료 
    public void HandlePotCookComplete(PotCookCompletePacket packet)
    {
        if (packet == null) return;

        pendingCookCompletePacket = packet;
        gaugeUI?.Hide();
        RemoveConsumedObj();
    }

    // IPunchReceiver: 솥 강제 배출 요청
    public void OnPunched(PlayerBrain player)
    {
        if (state == PotState.Ejecting) return;
        if (storedObjects.Count == 0 && trappedPlayers.Count == 0) return;
        RequestForceEject();
    }

    #endregion

}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(PotInteraction))]
public class PotInteractionEditor : UnityEditor.Editor
{
    // 기본 Inspector와 디버그 배출 버튼 표시
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(8f);

        bool previousEnabled = GUI.enabled;
        GUI.enabled = Application.isPlaying;

        if (GUILayout.Button("Debug Force Eject"))
            ((PotInteraction)target).DebugForceEject();

        GUI.enabled = previousEnabled;
    }
}
#endif
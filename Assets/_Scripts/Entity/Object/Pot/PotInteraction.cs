using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum PotState
{
    Idle,
    Cooking,
    Ejecting
}

public enum PotEjectType
{
    Completed,
    Forced
}

public class PotInteraction : MapObjInteraction, IPunchReceiver
{
    [Header("Storage")]
    [SerializeField] private Transform parentRecipe;
    [SerializeField] private Transform parentETC;

    [Header("Cooking")]
    [SerializeField] private InteractionGaugeUI gaugeUI;

    private readonly HashSet<CatchableObj> pendingObjects = new(); // 진입 대기 오브젝트 집합
    private readonly HashSet<CatchableObj> storedObjects = new(); // 보관 오브젝트 집합
    private readonly HashSet<long> receivedEntryIndices = new(); // 수신된 진입 인덱스 집합
    private readonly Dictionary<CatchableObj, long> entryIndices = new(); // 오브젝트별 진입 인덱스 매핑
    private readonly List<CatchableObj> recipeObjects = new(); // 조리 대상 오브젝트 리스트
    private readonly List<CatchableObj> etcObjects = new(); // 배출 대상 오브젝트 리스트
    private readonly HashSet<CatchableObj> idleAutoEjectObjects = new(); // 대기 중 자동 배출 오브젝트 집합
    private readonly List<PlayerBrain> trappedPlayers = new(); // 갇힌 플레이어 리스트
    private readonly Dictionary<PlayerBrain, int> playerOverlapCounts = new(); // 플레이어 겹침 횟수 추적

    private PotState state = PotState.Idle;

    private CatchableObj cookingBowl;

    private long cookingRecipeLastEntryIndex;
    private long lastProcessedEntryIndex;

    public PotState State => state;

    #region Lifecycle

    // 솥 활성화 초기화
    private void OnEnable()
    {
        receivedEntryIndices.Clear();
        lastProcessedEntryIndex = 0;
        ResetPotData();
    }

    // 솥 보관물 반환 및 초기화
    private void OnDisable()
    {
        StopPotCoroutines();
        ClearObjects();
        ResetPotData();

        receivedEntryIndices.Clear();
        lastProcessedEntryIndex = 0;
    }

    // 솥 데이터 초기화
    private void ResetPotData()
    {
        gaugeUI?.Hide();
        pendingObjects.Clear();
        storedObjects.Clear();
        entryIndices.Clear();
        recipeObjects.Clear();
        etcObjects.Clear();
        idleAutoEjectObjects.Clear();
        trappedPlayers.Clear();
        playerOverlapCounts.Clear();
        cookingBowl = null;
        cookingCoroutine = null;
        idleEjectCoroutine = null;
        ejectCoroutine = null;
        cookingRecipeLastEntryIndex = 0;
        state = PotState.Idle;
    }

    // 실행 중 솥 코루틴 중단
    private void StopPotCoroutines()
    {
        if (cookingCoroutine != null) StopCoroutine(cookingCoroutine);
        if (idleEjectCoroutine != null) StopCoroutine(idleEjectCoroutine);
        if (ejectCoroutine != null) StopCoroutine(ejectCoroutine);

        cookingCoroutine = null;
        idleEjectCoroutine = null;
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

    #endregion

    #region Trigger

    // 진입 대상 서버 요청
    private void OnTriggerEnter(Collider other)
    {
        if (TryGetPlayer(other, out PlayerBrain player))
        {
            RegisterPlayer(player);
            return;
        }

        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null) return;
        if (storedObjects.Contains(catchable) || !pendingObjects.Add(catchable)) return;

        RequestObjectEntry(catchable);
    }

    // 플레이어 이탈 추적
    private void OnTriggerExit(Collider other)
    {
        if (!TryGetPlayer(other, out PlayerBrain player)) return;
        if (!playerOverlapCounts.TryGetValue(player, out int overlapCount)) return;

        overlapCount--;
        if (overlapCount > 0)
        {
            playerOverlapCounts[player] = overlapCount;
            return;
        }

        playerOverlapCounts.Remove(player);
        trappedPlayers.Remove(player);
    }

    #endregion

    #region Server Stub

    // 솥 진입 패킷 전송 위치
    private void RequestObjectEntry(CatchableObj catchable)
    {
        // TODO: PotObjectEntryRequest 전송
        SimulateObjectEntry(catchable);
    }

    // 솥 진입 결과 수신 처리
    public void ReceiveObjectEntry(CatchableObj catchable, long entryIndex, PotState entryState)
    {
        if (catchable == null) return;

        pendingObjects.Remove(catchable);
        if (!receivedEntryIndices.Add(entryIndex)) return;
        if (storedObjects.Contains(catchable)) return;

        lastProcessedEntryIndex = System.Math.Max(lastProcessedEntryIndex, entryIndex);
        entryIndices[catchable] = entryIndex;

        if (entryState == PotState.Idle)
            HandleIdleEntry(catchable);
        else
            StoreETC(catchable, false);
    }

    // 솥 조리 시작 패킷 전송 위치
    private void RequestCookingStart(CatchableObj bowl)
    {
        // TODO: PotCookingStartRequest 전송
        SimulateCookingStart(bowl);
    }

    // 솥 조리 시작 결과 수신 처리
    public void ReceiveCookingStarted(CatchableObj bowl, long lastRecipeEntryIndex, float duration)
    {
        if (state != PotState.Idle) return;
        if (bowl == null || bowl != cookingBowl) return;
        if (recipeObjects.Count == 0) return;

        cookingRecipeLastEntryIndex = lastRecipeEntryIndex;
        ChangeState(PotState.Cooking);
        gaugeUI?.StartFill(duration);
        cookingCoroutine = StartCoroutine(CookingRoutine(duration));
    }

    // 강제 배출 패킷 전송 위치
    private void RequestForceEject()
    {
        // TODO: PotForceEjectRequest 전송
        SimulateForceEjectApproval();
    }

    // 강제 배출 승인 수신 처리
    public void ReceiveForceEjectApproved(long approvedEntryIndex)
    {
        if (state == PotState.Ejecting) return;
        lastProcessedEntryIndex = System.Math.Max(lastProcessedEntryIndex, approvedEntryIndex);
        BeginEjection(PotEjectType.Forced, null);
    }

    // 솥 조리 완료 결과 수신 처리
    public void ReceiveCookingCompleted(
        CatchableObj result,
        CatchableObj bowl,
        long completedEntryIndex)
    {
        if (state != PotState.Cooking) return;
        if (bowl == null || bowl != cookingBowl) return;

        lastProcessedEntryIndex = System.Math.Max(lastProcessedEntryIndex, completedEntryIndex);
        BeginEjection(PotEjectType.Completed, result);
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
            if (recipeObjects.Count == 0)
            {
                StoreETC(catchable, true);
                return;
            }

            StoreCookingBowl(catchable);
            RequestCookingStart(catchable);
            return;
        }

        StoreETC(catchable, true);
    }

    // 조리 대상 재료 보관
    private void StoreRecipe(CatchableObj catchable)
    {
        if (!TryStoreObject(catchable, parentRecipe)) return;

        recipeObjects.Add(catchable);
        SortByEntryIndex(recipeObjects);
    }

    // 조리 시작 그릇 보관
    private void StoreCookingBowl(CatchableObj bowl)
    {
        if (!TryStoreObject(bowl, parentRecipe)) return;
        cookingBowl = bowl;
    }

    // 배출 대상 오브젝트 보관
    private void StoreETC(CatchableObj catchable, bool autoEject)
    {
        if (!TryStoreObject(catchable, parentETC)) return;

        etcObjects.Add(catchable);
        SortByEntryIndex(etcObjects);

        if (!autoEject) return;
        idleAutoEjectObjects.Add(catchable);
        EnsureIdleEjectRoutine();
    }

    // 오브젝트 비활성 보관
    private bool TryStoreObject(CatchableObj catchable, Transform storageParent)
    {
        if (storageParent == null)
        {
            Debug.LogError("Pot storage parent is not assigned.");
            pendingObjects.Remove(catchable);
            entryIndices.Remove(catchable);
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

    // 진입 인덱스 기준 정렬
    private void SortByEntryIndex(List<CatchableObj> objects)
    {
        objects.Sort((left, right) => GetEntryIndex(left).CompareTo(GetEntryIndex(right)));
    }

    // 오브젝트 진입 인덱스 조회
    private long GetEntryIndex(CatchableObj catchable)
    {
        return catchable != null && entryIndices.TryGetValue(catchable, out long index)
            ? index
            : long.MaxValue;
    }

    // 마지막 조리 재료 인덱스 조회
    private long GetLastRecipeEntryIndex()
    {
        return recipeObjects.Count == 0 ? 0 : GetEntryIndex(recipeObjects[^1]);
    }

    #endregion

    #region Ejection

    [Header("Ejection")]
    [SerializeField] private Transform ejectPoint;
    [SerializeField, Min(0f)] private float ejectDelay = 1f;
    [SerializeField] private Vector3 ejectBaseDirection = new(0f, 1f, 1f);
    [SerializeField, Min(0f)] private float ejectRandomRangeX = 0.3f;
    [SerializeField] private float ejectRandomRangeYMin = -0.05f;
    [SerializeField] private float ejectRandomRangeYMax = 0.2f;
    [SerializeField, Min(0f)] private float ejectRandomRangeZ = 0.3f;
    [SerializeField, Min(0f)] private float ejectForce = 5f;
    [SerializeField, Min(0f)] private float bowlEjectForce = 4f;
    [SerializeField, Min(0f)] private float playerEjectForce = 8f;
    [SerializeField, Min(0.1f)] private float ejectGizmoLength = 2f;

    private Coroutine idleEjectCoroutine;
    private Coroutine ejectCoroutine;

    // Idle 자동 배출 코루틴 시작
    private void EnsureIdleEjectRoutine()
    {
        if (idleEjectCoroutine != null) return;
        idleEjectCoroutine = StartCoroutine(IdleEjectRoutine());
    }

    // Idle 진입 오브젝트 순차 배출
    private IEnumerator IdleEjectRoutine()
    {
        while (TryGetNextIdleEjectObject(out CatchableObj catchable))
        {
            yield return new WaitForSeconds(ejectDelay);

            if (state == PotState.Ejecting) break;
            if (!idleAutoEjectObjects.Remove(catchable)) continue;
            if (!storedObjects.Contains(catchable)) continue;

            EjectObject(catchable, ejectForce);
        }

        idleEjectCoroutine = null;
    }

    // 다음 Idle 자동 배출 대상 조회
    private bool TryGetNextIdleEjectObject(out CatchableObj result)
    {
        result = null;

        foreach (CatchableObj catchable in etcObjects)
        {
            if (catchable == null || !idleAutoEjectObjects.Contains(catchable)) continue;
            result = catchable;
            return true;
        }

        return false;
    }

    // 솥 배출 단계 시작
    private void BeginEjection(PotEjectType ejectType, CatchableObj result)
    {
        if (cookingCoroutine != null)
        {
            StopCoroutine(cookingCoroutine);
            cookingCoroutine = null;
        }

        if (idleEjectCoroutine != null)
        {
            StopCoroutine(idleEjectCoroutine);
            idleEjectCoroutine = null;
        }

        gaugeUI?.Hide();
        ChangeState(PotState.Ejecting);
        ejectCoroutine = StartCoroutine(EjectRoutine(ejectType, result));
    }

    // 배출 우선순위 순차 처리
    private IEnumerator EjectRoutine(PotEjectType ejectType, CatchableObj result)
    {
        if (ejectType == PotEjectType.Completed)
            CompleteRecipe(result);

        while (true)
        {
            if (TryEjectPlayer())
            {
                yield return new WaitForSeconds(ejectDelay);
                continue;
            }

            CatchableObj next = GetNextEjectObject(ejectType);
            if (next != null)
            {
                bool isCompletedBowl =
                    ejectType == PotEjectType.Completed &&
                    result != null &&
                    next == cookingBowl;
                float force = isCompletedBowl ? bowlEjectForce : ejectForce;
                EjectObject(next, force);
                yield return new WaitForSeconds(ejectDelay);
                continue;
            }

            if (trappedPlayers.Count == 0 && storedObjects.Count == 0) break;
        }

        ejectCoroutine = null;
        ResetPotData();
    }

    // 정상 완료 재료 소비 및 결과 귀속
    private void CompleteRecipe(CatchableObj result)
    {
        CatchableObj[] ingredients = recipeObjects.ToArray();
        foreach (CatchableObj ingredient in ingredients)
        {
            RemoveStoredObject(ingredient);
            if (ingredient == null) continue;

            if (ObjectPoolManager.Instance != null)
                ObjectPoolManager.Instance.Push(ingredient.gameObject);
        }

        recipeObjects.Clear();

        if (result == null || cookingBowl == null) return;
        if (!cookingBowl.TryGetComponent(out PlateInteraction plate)) return;
        plate.HandleEntityAdded(result);
    }

    // 다음 배출 대상 우선순위 조회
    private CatchableObj GetNextEjectObject(PotEjectType ejectType)
    {
        if (ejectType == PotEjectType.Forced && recipeObjects.Count > 0)
            return recipeObjects[0];

        if (cookingBowl != null && storedObjects.Contains(cookingBowl))
            return cookingBowl;

        foreach (CatchableObj catchable in etcObjects)
        {
            if (catchable != null && storedObjects.Contains(catchable))
                return catchable;
        }

        return null;
    }

    // 보관 오브젝트 물리 배출
    private void EjectObject(CatchableObj catchable, float force)
    {
        if (catchable == null) return;

        RemoveStoredObject(catchable);
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
        catchable.Rb.AddForce(GetEjectDirection() * force, ForceMode.Impulse);
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
        entryIndices.Remove(catchable);
        recipeObjects.Remove(catchable);
        etcObjects.Remove(catchable);
        idleAutoEjectObjects.Remove(catchable);

        if (cookingBowl == catchable)
            cookingBowl = null;
    }

    // 랜덤 편차 적용 배출 방향 계산
    private Vector3 GetEjectDirection()
    {
        float randomYMin = Mathf.Min(ejectRandomRangeYMin, ejectRandomRangeYMax);
        float randomYMax = Mathf.Max(ejectRandomRangeYMin, ejectRandomRangeYMax);
        Vector3 randomOffset = new(
            Random.Range(-ejectRandomRangeX, ejectRandomRangeX),
            Random.Range(randomYMin, randomYMax),
            Random.Range(-ejectRandomRangeZ, ejectRandomRangeZ));

        Vector3 localDirection = ejectBaseDirection + randomOffset;
        if (localDirection.sqrMagnitude < 0.0001f)
            localDirection = ejectBaseDirection.sqrMagnitude >= 0.0001f
                ? ejectBaseDirection
                : Vector3.up;

        return transform.TransformDirection(localDirection).normalized;
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
        BeginEjection(PotEjectType.Forced, null);
    }

    #endregion

    #region Player

    // 플레이어 진입 등록
    private void RegisterPlayer(PlayerBrain player)
    {
        int overlapCount = playerOverlapCounts.GetValueOrDefault(player, 0) + 1;
        playerOverlapCounts[player] = overlapCount;

        if (overlapCount > 1) return;

        trappedPlayers.Add(player);
        if (trappedPlayers.Count >= 2 && state != PotState.Ejecting)
            RequestForceEject();
    }

    // 우선순위 플레이어 배출
    private bool TryEjectPlayer()
    {
        while (trappedPlayers.Count > 0)
        {
            PlayerBrain player = trappedPlayers[0];
            trappedPlayers.RemoveAt(0);

            if (player == null) continue;

            playerOverlapCounts.Remove(player);
            if (player.Interact?.HeldObj != null)
            {
                CatchableObj heldObject = player.Interact.HeldObj;
                player.Interact.ForceDropHeld();

                if (player.Interact.TryReleaseHeld(heldObject))
                    heldObject.OnDrop();
            }

            player.EffectController?.ApplyKnockback(GetEjectDirection() * playerEjectForce);
            return true;
        }

        return false;
    }

    // 플레이어 부모 컴포넌트 탐색
    private static bool TryGetPlayer(Collider other, out PlayerBrain player)
    {
        player = other.GetComponentInParent<PlayerBrain>();
        return player != null;
    }

    // 빈손 주먹 강제 배출 요청
    public void OnPunched(PlayerBrain player)
    {
        if (state == PotState.Ejecting) return;
        if (storedObjects.Count == 0 && trappedPlayers.Count == 0) return;
        RequestForceEject();
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

    #region Test

    [Header("Test")]
    [SerializeField, Min(0f)] private float cookingDuration = 3f;
    [SerializeField] private int dummyResultFoodId = 99999;

    private Coroutine cookingCoroutine;
    private long nextDummyEntryIndex = 1;
    private long nextDummyEntityId = -1;

    // 더미 솥 진입 결과 생성
    private void SimulateObjectEntry(CatchableObj catchable)
    {
        ReceiveObjectEntry(catchable, nextDummyEntryIndex++, state);
    }

    // 더미 솥 조리 시작 결과 생성
    private void SimulateCookingStart(CatchableObj bowl)
    {
        ReceiveCookingStarted(bowl, GetLastRecipeEntryIndex(), cookingDuration);
    }

    // 더미 강제 배출 승인 생성
    private void SimulateForceEjectApproval()
    {
        ReceiveForceEjectApproved(lastProcessedEntryIndex);
    }

    // 더미 조리 시간 진행
    private IEnumerator CookingRoutine(float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, duration));
        cookingCoroutine = null;
        SimulateCookingCompleted();
    }

    // 더미 조리 완료 결과 생성
    private void SimulateCookingCompleted()
    {
        ReceiveCookingCompleted(
            CreateDummyResult(dummyResultFoodId),
            cookingBowl,
            lastProcessedEntryIndex);
    }

    // 더미 완성 음식 생성
    private CatchableObj CreateDummyResult(int foodId)
    {
        if (DataManager.Instance == null || ObjectPoolManager.Instance == null) return null;

        Ingredient data = DataManager.Instance.GetIngredient().GetData(foodId);
        if (data == null || string.IsNullOrEmpty(data.prefabName)) return null;

        GameObject resultObject = ObjectPoolManager.Instance.Pop(
            data.prefabName,
            transform.position,
            transform.rotation);

        if (resultObject == null || !resultObject.TryGetComponent(out CatchableObj result)) return null;

        result.NetworkId = nextDummyEntityId--;
        result.Data = data;
        return result;
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

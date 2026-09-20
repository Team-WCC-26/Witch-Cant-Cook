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

public class PotInteraction : MapObjInteraction
{
    [SerializeField] private InteractionGaugeUI gaugeUI;
    [SerializeField] private Transform parentRecipe;
    [SerializeField] private Transform parentETC;

    private readonly HashSet<CatchableObj> storedObjects = new(); // 보관 오브젝트 집합
    private readonly List<CatchableObj> recipeObjects = new(); // 조리 대상 오브젝트 리스트
    private readonly Queue<CatchableObj> ejectQueue = new(); // 배출 대상 오브젝트 큐
    private readonly List<PlayerBrain> trappedPlayers = new(); // 갇힌 플레이어 리스트
    private readonly Dictionary<PlayerBrain, int> playerOverlapCounts = new(); // 플레이어 겹침 횟수 추적

    private PotState state = PotState.Idle;

    public PotState State => state;

    #region Lifecycle

    // 솥 활성화 초기화
    private void OnEnable()
    {
        ResetPotData();
    }

    // 솥 보관물 반환 및 초기화
    private void OnDisable()
    {
        ClearObjects();
        ResetPotData();
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

    // 솥 데이터 초기화
    private void ResetPotData()
    {
        gaugeUI?.Hide();

        storedObjects.Clear();
        recipeObjects.Clear();
        ejectQueue.Clear();

        trappedPlayers.Clear();
        playerOverlapCounts.Clear();

        state = PotState.Idle;
    }

    #endregion

    #region Trigger

    // 진입 대상 분류
    private void OnTriggerEnter(Collider other)
    {
        if (TryGetPlayer(other, out PlayerBrain player))
        {
            RegisterPlayer(player);
            return;
        }

        CatchableObj catchable = other.GetComponentInParent<CatchableObj>();
        if (catchable == null || storedObjects.Contains(catchable)) return;

        switch (state)
        {
            case PotState.Idle:
                HandleIdleEntry(catchable);
                break;

            case PotState.Cooking:
            case PotState.Ejecting:
                StoreETC(catchable);
                break;
        }
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

    #region Object Storage

    // Idle 상태에서 진입한 오브젝트 분류
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
                StoreETC(catchable);
                return;
            }

            StoreRecipe(catchable);
            ChangeState(PotState.Cooking);
            return;
        }

        StoreETC(catchable);
    }

    // 조리 대상 보관
    private void StoreRecipe(CatchableObj catchable)
    {
        if (parentRecipe == null)
        {
            Debug.LogError("Pot ParentRecipe is not assigned.");
            return;
        }

        if (!storedObjects.Add(catchable)) return;

        recipeObjects.Add(catchable);
        StoreObject(catchable, parentRecipe);
    }

    // 배출 대상 보관
    private void StoreETC(CatchableObj catchable)
    {
        if (parentETC == null)
        {
            Debug.LogError("Pot ParentETC is not assigned.");
            return;
        }

        if (!storedObjects.Add(catchable)) return;

        ejectQueue.Enqueue(catchable);
        StoreObject(catchable, parentETC);
    }

    // 비활성 오브젝트 보관
    private static void StoreObject(CatchableObj catchable, Transform parent)
    {
        catchable.ChangePickState(false);
        catchable.SetPhysicsState(false);
        catchable.transform.SetParent(parent, false);
        catchable.gameObject.SetActive(false);
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
        if (trappedPlayers.Count >= 2)
            ChangeState(PotState.Ejecting);
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
}

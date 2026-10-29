using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 풀링 가능한 오브젝트를 나타내는 인터페이스.
/// </summary>
public interface IPoolable
{
    void ResetForPool();
}

public class ObjectPoolManager : Singleton<ObjectPoolManager>
{
    public Dictionary<long, Object> activeObjDict = new();
    private readonly Dictionary<string, Queue<GameObject>> _poolDic = new();
    private readonly Dictionary<string, Vector3> _prefabLocalScales = new();
    private readonly Dictionary<GameObject, SpawnComponents> _components = new();
    private static readonly Unity.Profiling.ProfilerMarker SpawnMarker = new("Ingredient.ObjectPool.Spawn");
    private static readonly Unity.Profiling.ProfilerMarker CreateMarker = new("Ingredient.ObjectPool.Create");

    private sealed class SpawnComponents
    {
        public readonly CatchableObj Catchable;
        public readonly IngredientReaction Reaction;
        public readonly List<MonoBehaviour> Behaviours = new();
        public readonly List<Collider> Colliders = new();
        public SpawnComponents(GameObject go)
        {
            Catchable = go.GetComponent<CatchableObj>();
            Reaction = go.GetComponent<IngredientReaction>();
        }
    }

    private SpawnComponents GetSpawnComponents(GameObject go)
    {
        if (!_components.TryGetValue(go, out SpawnComponents cached))
            _components.Add(go, cached = new SpawnComponents(go));
        return cached;
    }

    public int GetPooledCount(string key) => _poolDic.TryGetValue(key, out var queue) ? queue.Count : 0;

    // 풀 관리용 루트 트랜스폼
    private Transform _poolRoot;

    private void Start()
    {
        InitRoot();
    }
    public void InitRoot()
    {
        if (_poolRoot == null)
        {
            // 여기 DontDestroyOnLoad일 필요가 없지않나
            _poolRoot = new GameObject("@ObjectPool_Root").transform;
            DontDestroyOnLoad(_poolRoot);
        }
    }

    public GameObject Pop(string key, Transform spawnPoint = null)
    {
        if (!_poolDic.TryGetValue(key, out var queue))
        {
            queue = new Queue<GameObject>();
            _poolDic[key] = queue;
        }

        GameObject go = (queue.Count > 0) ? queue.Dequeue() : CreateNewInstance(key);

        if (go != null)
        {
            RestorePrefabScale(go, key);

            if (spawnPoint != null)
            {
                go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            }

            go.SetActive(true);
        }

        return go;
    }
    public GameObject Pop(string key, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        GameObject go = Pop(key, parent);
        if (go != null)
        {
            if (go.TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            go.transform.SetPositionAndRotation(position, rotation);
        }
        return go;
    }

    public GameObject Pop(string key, Vector3 position, Quaternion rotation,
        out CatchableObj catchable, out IngredientReaction reaction)
    {
        using var scope = SpawnMarker.Auto();
        GameObject go = Pop(key, position, rotation);
        catchable = null;
        reaction = null;
        if (go != null)
        {
            SpawnComponents cached = GetSpawnComponents(go);
            catchable = cached.Catchable;
            reaction = cached.Reaction;
        }
        return go;
    }

    public void Push(GameObject go)
    {
        if (go == null) return;

        ReleaseNetworkBoundState(go);

        string key = go.name;
        if (_poolDic.TryGetValue(key, out var queue))
        {
            InitRoot(); //방어코드

            go.SetActive(false);
            ResetPoolable(go);
            go.transform.SetParent(_poolRoot);
            // Restore after reparenting, which otherwise preserves the pot's world scale.
            RestorePrefabScale(go, key);
            queue.Enqueue(go);
        }
        else
        {
            Destroy(go);
        }
    }

    public void ClearPool(string key)
    {
        if (_poolDic.TryGetValue(key, out var queue))
        {
            while (queue.Count > 0)
            {
                GameObject go = queue.Dequeue();
                _components.Remove(go);
                UnityEngine.Object.Destroy(go);
            }
            _poolDic.Remove(key);
        }
    }

    public void ReturnRoomObjects()
    {
        // Active room entities are also parented below the persistent pool root.
        if (_poolRoot != null)
        {
            foreach (CatchableObj item in _poolRoot.GetComponentsInChildren<CatchableObj>(true))
                if (item != null && item.gameObject.activeInHierarchy) Push(item.gameObject);
        }
        activeObjDict.Clear();
    }

    private GameObject CreateNewInstance(string key)
    {
        using var scope = CreateMarker.Auto();
        GameObject prefab = ResourceManager.Instance.GetAsset<GameObject>(key);
        if (prefab == null)
        {
            Debug.LogError($"[Pool] Prefab is not loaded for key: {key}");
            return null;
        }

        InitRoot(); // 생성할 때 루트가 있는지 확인

        _prefabLocalScales[key] = prefab.transform.localScale;

        // 최초 생성 시점에도 루트 오브젝트 밑에 배치되도록 설정
        GameObject go = UnityEngine.Object.Instantiate(prefab, _poolRoot);
        go.name = key; // 이름을 키값으로 강제 고정
        ResetPoolable(go);
        return go;
    }

    private void RestorePrefabScale(GameObject go, string key)
    {
        if (_prefabLocalScales.TryGetValue(key, out Vector3 scale))
            go.transform.localScale = scale;
    }

    /// <summary>
    /// 지정한 개수만큼 미리 인스턴스를 생성해서 풀에 저장한다.
    /// </summary>
    public void PrewarmPool(string key, int count)
    {
        // Push must find the queue; otherwise it destroys the just-created prewarm object.
        if (count <= 0) return;
        if (!_poolDic.ContainsKey(key)) _poolDic.Add(key, new Queue<GameObject>(count));
        for (int i = 0; i < count; i++)
        {
            GameObject go = CreateNewInstance(key);
            if (go != null)
            {
                Push(go); // 생성 후 바로 큐에 넣음
            }
        }
    }

    /// <summary>
    /// 풀에서 재사용하기 전에 상태 초기화
    /// </summary>
    private void ResetPoolable(GameObject go)
    {
        SpawnComponents cached = GetSpawnComponents(go);
        // Refresh lists so dynamically added components still follow the existing reset contract.
        go.GetComponents(cached.Behaviours);
        foreach (MonoBehaviour behaviour in cached.Behaviours)
        {
            if (behaviour is IPoolable poolable)
            {
                poolable.ResetForPool();
            }
        }

        if (go.TryGetComponent(out Rigidbody rb))
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.useGravity = true;
            rb.Sleep();
        }

        go.GetComponents(cached.Colliders);
        foreach (Collider collider in cached.Colliders)
        {
            collider.enabled = true;
        }
    }

    /// <summary>
    /// 모든 풀 반환 경로에서 기존 컨베이어 등록을 먼저 정리합니다.
    /// 오브젝트가 비활성화된 뒤에도 이전 벨트가 같은 Transform을 이동시키는 것을 방지합니다.
    /// </summary>
    private static void ReleaseNetworkBoundState(GameObject go)
    {
        if (!go.TryGetComponent(out CatchableObj catchable)) return;

        long networkId = catchable.NetworkId;
        if (networkId <= 0) return;

        if (ConveyorBeltRegistry.TryGetOwner(networkId, out ConveyorBeltController belt))
        {
            belt.UnregisterItem(networkId);
        }

    }
}

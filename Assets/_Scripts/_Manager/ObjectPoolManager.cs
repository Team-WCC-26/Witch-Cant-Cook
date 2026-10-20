using System.Collections.Generic;
using Unity.Profiling;
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
    // Optional observers for isolated legacy comparisons. Normal gameplay has no subscribers.
    public event System.Action<GameObject> ObjectPrewarmed;
    public event System.Action<GameObject> ObjectDiscarding;
    public event System.Action<long> NetworkStateReleased;
    public void NotifyNetworkStateReleased(long id) => NetworkStateReleased?.Invoke(id);
    private static readonly ProfilerMarker SpawnMarker = new("Ingredient.ObjectPool.Spawn");
    private static readonly ProfilerMarker CreateMarker = new("Ingredient.ObjectPool.Create");
    public const string SpawnPreparationVersion = "PrewarmAndNonAllocReset_v1";
    public Dictionary<long, Object> activeObjDict = new();
    private readonly Dictionary<string, Queue<GameObject>> _poolDic = new();
    private readonly Dictionary<string, Vector3> _prefabLocalScales = new();
    private readonly Dictionary<GameObject, SpawnComponents> _components = new();

    private sealed class SpawnComponents
    {
        public readonly Rigidbody Body;
        public readonly CatchableObj Catchable;
        public readonly IngredientReaction Reaction;
        // Refresh these lists on reset: dishes can acquire FoodRecipeIdentity at runtime.
        public readonly List<MonoBehaviour> Behaviours = new(8);
        public readonly List<Collider> Colliders = new(4);
        public SpawnComponents(GameObject go)
        {
            Body = go.GetComponent<Rigidbody>();
            Catchable = go.GetComponent<CatchableObj>();
            Reaction = go.GetComponent<IngredientReaction>();
        }
    }

    private SpawnComponents GetSpawnComponents(GameObject go)
    {
        if (!_components.TryGetValue(go, out var components))
            _components.Add(go, components = new SpawnComponents(go));
        return components;
    }

    private Queue<GameObject> GetQueue(string key, int capacity = 0)
    {
        if (!_poolDic.TryGetValue(key, out var queue))
            _poolDic.Add(key, queue = new Queue<GameObject>(capacity));
        return queue;
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
        var queue = GetQueue(key);

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
        => Pop(key, position, rotation, out _, out _, parent);

    // Reuse the component references resolved when the pool instance was prepared.
    public GameObject Pop(string key, Vector3 position, Quaternion rotation,
        out CatchableObj catchable, out IngredientReaction reaction, Transform parent = null)
    {
        using var spawnScope = SpawnMarker.Auto();
        GameObject go = Pop(key, parent);
        catchable = null;
        reaction = null;
        if (go != null)
        {
            var components = GetSpawnComponents(go);
            catchable = components.Catchable;
            reaction = components.Reaction;
            Rigidbody rb = components.Body;
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            go.transform.SetPositionAndRotation(position, rotation);
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
            ObjectDiscarding?.Invoke(go);
            _components.Remove(go);
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
                if (go != null) ObjectDiscarding?.Invoke(go);
                if (!ReferenceEquals(go, null)) _components.Remove(go);
                UnityEngine.Object.Destroy(go);
            }
            _poolDic.Remove(key);
        }
    }

    private GameObject CreateNewInstance(string key)
    {
        using var createScope = CreateMarker.Auto();
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
        if (count <= 0) return;
        // Push must find this queue even when this key has never been spawned.
        GetQueue(key, count);
        activeObjDict.EnsureCapacity(activeObjDict.Count + count);
        for (int i = 0; i < count; i++)
        {
            GameObject go = CreateNewInstance(key);
            if (go != null)
            {
                Push(go); // 생성 후 바로 큐에 넣음
                ObjectPrewarmed?.Invoke(go);
            }
        }
    }

    /// <summary>
    /// 풀에서 재사용하기 전에 상태 초기화
    /// </summary>
    private void ResetPoolable(GameObject go)
    {
        var components = GetSpawnComponents(go);
        go.GetComponents(components.Behaviours);
        foreach (MonoBehaviour behaviour in components.Behaviours)
        {
            if (behaviour is IPoolable poolable)
            {
                poolable.ResetForPool();
            }
        }

        Rigidbody rb = components.Body;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.Sleep();
        }

        go.GetComponents(components.Colliders);
        foreach (Collider collider in components.Colliders)
        {
            collider.enabled = true;
        }
    }

    /// <summary>
    /// 모든 풀 반환 경로에서 컨베이어의 NetworkID 상태를 먼저 정리합니다.
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

        ObjectPoolManager.Instance.NotifyNetworkStateReleased(networkId);
    }
}

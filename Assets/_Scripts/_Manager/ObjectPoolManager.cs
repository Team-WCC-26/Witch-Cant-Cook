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
            if (go.TryGetComponent(out Rigidbody rb))
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

        string key = go.name;
        if (_poolDic.TryGetValue(key, out var queue))
        {
            InitRoot(); //방어코드

            ResetPoolable(go);

            go.SetActive(false);
            go.transform.SetParent(_poolRoot);
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
                UnityEngine.Object.Destroy(queue.Dequeue());
            }
            _poolDic.Remove(key);
        }
    }

    private GameObject CreateNewInstance(string key)
    {
        GameObject prefab = ResourceManager.Instance.GetAsset<GameObject>(key);
        if (prefab == null)
        {
            Debug.LogError($"[Pool] Prefab is not loaded for key: {key}");
            return null;
        }

        InitRoot(); // 생성할 때 루트가 있는지 확인

        // 최초 생성 시점에도 루트 오브젝트 밑에 배치되도록 설정
        GameObject go = UnityEngine.Object.Instantiate(prefab, _poolRoot);
        go.name = key; // 이름을 키값으로 강제 고정
        ResetPoolable(go);
        return go;
    }

    /// <summary>
    /// 지정한 개수만큼 미리 인스턴스를 생성해서 풀에 저장한다.
    /// </summary>
    public void PrewarmPool(string key, int count)
    {
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
    private static void ResetPoolable(GameObject go)
    {
        foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
        {
            if (behaviour is IPoolable poolable)
            {
                poolable.ResetForPool();
            }
        }

        if (go.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.Sleep();
        }

        foreach (Collider collider in go.GetComponents<Collider>())
        {
            collider.enabled = true;
        }
    }
}

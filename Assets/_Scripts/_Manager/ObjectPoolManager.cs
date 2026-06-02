using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

public class ObjectPoolManager : Singleton<ObjectPoolManager>
{
    public Dictionary<long, UnityEngine.Object> activeObjDict = new();
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

    public GameObject Pop(string key, Transform parent = null)
    {
        if (!_poolDic.TryGetValue(key, out var queue))
        {
            queue = new Queue<GameObject>();
            _poolDic[key] = queue;
        }

        GameObject go = (queue.Count > 0) ? queue.Dequeue() : CreateNewInstance(key);

        if (go != null)
        {
            go.transform.SetParent(parent);
            go.SetActive(true);
        }
        return go;
    }

    public GameObject Pop(string key, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        GameObject go = Pop(key, parent);
        if (go != null)
        {
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
            InitRoot(); // 반납할 때 루트가 있는지 확인

            go.SetActive(false);
            go.transform.SetParent(_poolRoot);
            queue.Enqueue(go);
        }
        else
        {
            UnityEngine.Object.Destroy(go);
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
            Debug.Log($"[Pool Create] key : {key}");
            Debug.LogError($"[Pool] 리소스 매니저에 '{key}' 에셋이 로드되어 있지 않습니다.");
            return null;
        }

        InitRoot(); // 생성할 때 루트가 있는지 확인

        // 최초 생성 시점에도 루트 오브젝트 밑에 배치되도록 설정
        GameObject go = UnityEngine.Object.Instantiate(prefab, _poolRoot);
        go.name = key; // 이름을 키값으로 강제 고정
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
        Debug.Log($"[Pool] {key} 풀이 {count}개만큼 프리웜되었습니다.");
    }

}
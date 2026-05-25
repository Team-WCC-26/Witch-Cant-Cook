using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ObjectPoolManager : Singleton<ObjectPoolManager>
{
    private readonly Dictionary<string, Queue<GameObject>> _poolDic = new();

    // 풀 관리용 루트 트랜스폼
    private Transform _poolRoot;

    public void InitRoot()
    {
        if (_poolRoot == null)
        {
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

    public void Push(GameObject go)
    {
        if (go == null) return;

        string key = go.name;

        if (_poolDic.TryGetValue(key, out var queue))
        {
            InitRoot(); // 반납할 때 루트가 있는지 확인

            go.SetActive(false);
            go.transform.SetParent(_poolRoot); // @ObjectPool_Root 밑으로 깔끔하게 수납!
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
            Debug.LogError($"[Pool] 리소스 매니저에 '{key}' 에셋이 로드되어 있지 않습니다.");
            return null;
        }

        InitRoot(); // 생성할 때 루트가 있는지 확인

        // 최초 생성 시점에도 루트 오브젝트 밑에 배치되도록 설정
        GameObject go = UnityEngine.Object.Instantiate(prefab, _poolRoot);
        go.name = key; // 이름을 키값으로 강제 고정
        return go;
    }

}
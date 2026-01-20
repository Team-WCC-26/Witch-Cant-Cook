using System;
using UnityEngine;

public abstract class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    private static readonly object lockObj = new();


    [Tooltip("Scene 이동 시 파괴 여부")]
    [SerializeField] protected bool isDestroyOnLoad = true;
    [HideInInspector] public static bool IsInitialized => instance != null;

    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                Debug.LogWarning($"{typeof(T).Name} Instance requested but not initialized. Create it via GameManager.");
            }
            return instance;
        }
    }

    protected virtual void Awake()
    {
        lock(lockObj)
        {
            if (instance == null)
            {
                instance = this as T;
                if (!isDestroyOnLoad)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        lock (lockObj)
        {
            if (instance == this)
                instance = null;
        }
    }
}
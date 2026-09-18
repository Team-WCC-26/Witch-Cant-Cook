using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ResourceManager : Singleton<ResourceManager>
{
    // 알맹이
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    // 로드된 에셋을 관리하는 캐시 딕셔너리
    private Dictionary<string, AsyncOperationHandle> _handleCache = new Dictionary<string, AsyncOperationHandle>();


    protected override void Awake()
    {
        base.Awake();
        if (IsInitialized)
        {
            Debug.LogWarning("[ResourceManager] Duplicate instance initialization was prevented.");
            return;
        }
    }

    public async UniTask Init()
    {
        await InitializeAsync();
        await PreloadAssetsAsync();
    }

    /// <summary>
    /// Addressables 시스템 초기화
    /// </summary>
    public async UniTask InitializeAsync()
    {
        var initHandle = Addressables.InitializeAsync();

        await initHandle.ToUniTask();

    }

    /// <summary>
    /// 초기 preload 리소스 로드
    /// </summary>
    /// <returns></returns>
    private async UniTask PreloadAssetsAsync()
    {
        // 전체 preload
        await LoadAllAddressablesAsync();

    }

    /// <summary>
    /// 캐시된 에셋 가져오기
    /// </summary>
    public T GetAsset<T>(string key) where T : UnityEngine.Object
    {
        if (_assetCache.TryGetValue(key, out var asset))
        {
            return asset as T;
        }

        Debug.LogWarning($"[ResourceManager] Asset is not cached: {key}");
        return null;
    }

    /// <summary>
    /// 개별 에셋 로드
    /// </summary>
    public async UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
    {
        // 이미 캐시에 있는 경우
        if (_assetCache.TryGetValue(key, out var cachedAsset))
        {
            return cachedAsset as T;
        }

        var handle = Addressables.LoadAssetAsync<T>(key);
        try
        {
            await handle.ToUniTask();
            if (handle.Status == AsyncOperationStatus.Failed)
            {
                Debug.LogError($"[ResourceManager] Failed to load asset: {key}");
                return null;
            }

            // 두 dict 상태 동기화
            _handleCache.Add(key, handle);
            _assetCache.Add(key, handle.Result);

            return handle.Result;

        }
        catch (Exception e)
        {
            Debug.LogError($"[ResourceManager] Failed to load asset: {key} - {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 모든 Addressable 에셋 로드
    /// </summary>
    public async UniTask<bool> LoadAllAddressablesAsync()
    {
        List<UniTask> loadTasks = new();

        foreach (var locator in Addressables.ResourceLocators)
        {
            foreach (var key in locator.Keys)
            {
                string address = key.ToString();

                // 이미 캐시에 있으면 스킵
                if (_assetCache.ContainsKey(address))
                    continue;

                loadTasks.Add(LoadAsync<UnityEngine.Object>(address));
            }
        }

        await UniTask.WhenAll(loadTasks);

        return true;
    }


    /// <summary>
    /// 개별 에셋 해제
    /// </summary>
    public void Release(string key)
    {
        // 핸들 확인
        if (_handleCache.TryGetValue(key, out AsyncOperationHandle handle))
        {
            Addressables.Release(handle); // 어드레서블 메모리 해제

            _handleCache.Remove(key);     
            _assetCache.Remove(key);      

        }
    }


    /// <summary>
    /// 전체 에셋 해제
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var handle in _handleCache.Values)
        {
            Addressables.Release(handle);
        }

        _handleCache.Clear();
        _assetCache.Clear();

    }


}

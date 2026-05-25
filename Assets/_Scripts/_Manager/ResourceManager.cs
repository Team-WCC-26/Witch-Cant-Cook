using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class ResourceManager : Singleton<ResourceManager>
{
    // 알맹이
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    // 로드된 에셋을 관리하는 캐시 딕셔너리
    private Dictionary<string, AsyncOperationHandle> _handleCache = new Dictionary<string, AsyncOperationHandle>();

    // 싱글톤 혹은 필요한 곳에서 호출할 수 있도록 초기화
    public void Init()
    {
        
    }
    public T GetAsset<T>(string key) where T : UnityEngine.Object
    {
        if (_assetCache.TryGetValue(key, out var asset))
        {
            return asset as T;
        }

        Debug.Log($"[ResourceManager] Object 요청: {key}");
        return null;
    }

    public async UniTask<T> LoadAsync<T>(string key) where T : UnityEngine.Object
    {
        // 1. 오브젝트 캐시에 있으면 
        if (_assetCache.TryGetValue(key, out var cachedAsset))
        {
            return cachedAsset as T;
        }

        var asyncOperation = Addressables.LoadAssetAsync<T>(key);
        try
        {
            await asyncOperation.ToUniTask();
            if (asyncOperation.Status == AsyncOperationStatus.Succeeded)
            {
                // 두 dict 상태 동기화
                _handleCache.Add(key, asyncOperation);
                _assetCache.Add(key, asyncOperation.Result);

                return asyncOperation.Result;
            }
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"[ResourceManager] 에셋 로드 예외: {key} - {e.Message}");
            return null;
        }
    }

    public void Release(string key)
    {
        // 핸들 확인
        if (_handleCache.TryGetValue(key, out AsyncOperationHandle handle))
        {
            Addressables.Release(handle); // 어드레서블 메모리 해제

            _handleCache.Remove(key);     
            _assetCache.Remove(key);      

            Debug.Log($"[ResourceManager] 에셋 캐시 및 메모리 완전 해제: {key}");
        }
    }

    // 1. 에셋 로드 함수
    public async UniTask<bool> LoadAddressableAsync()
    {
        var handle = Addressables.InitializeAsync();
        await handle.ToUniTask();

        if (handle.Status == AsyncOperationStatus.Failed)
        {
            Debug.LogError("[ResourceManager] Addressables 로드 및 초기화 실패");
            return false;
        }

        Debug.Log("[ResourceManager] Addressables 시스템 초기화 완료");
        return true;
    }



}

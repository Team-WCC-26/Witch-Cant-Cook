using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ResourceManager : Singleton<ResourceManager>
{
    // 알맹이
    private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();

    // 로드된 에셋을 관리하는 캐시 딕셔너리
    private Dictionary<string, AsyncOperationHandle> _handleCache = new Dictionary<string, AsyncOperationHandle>();

    [SerializeField] private List<string> addressableLabelToPreload = new List<string> { "Ingredients", "Tools" };
    public List<string> AddressableLabelToPreload => addressableLabelToPreload;

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
        foreach (var label in addressableLabelToPreload)
        {
            var assets = await LoadAssetsByLabelAsync<UnityEngine.GameObject>(label);

            if (assets.Count == 0)
            {
                Debug.LogError($"[ResourceManager] Addressables 로드 실패 - Label : {label}");
                return false;
            }
        }


        Debug.Log("[ResourceManager] Addressables 시스템 초기화 완료");
        return true;
    }
    public async UniTask<IList<T>> LoadAssetsByLabelAsync<T>(string label) where T : UnityEngine.Object
    {
        // 라벨로 모든 에셋 로드
        var handle = Addressables.LoadAssetsAsync<T>(label, (asset) =>
        {
            Debug.Log($"[Cache Register] {asset.name}");

            // 로드된 에셋들을 캐시에 등록 (이미 있으면 무시)
            if (!_assetCache.ContainsKey(asset.name))
            {
                _assetCache[asset.name] = asset;
            }

        });

        if (!_handleCache.ContainsKey(label))
        {
            _handleCache[label] = handle;
        }
        await handle.ToUniTask();

        return handle.Result;
    }

}

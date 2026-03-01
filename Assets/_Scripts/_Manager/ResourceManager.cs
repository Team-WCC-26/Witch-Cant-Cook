using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class ResourceManager : Singleton<ResourceManager>
{
    // 캐싱
    private readonly Dictionary<string, Object> resourceCache = new();

    // Addressable 사용
    private readonly Dictionary<string, Object> addressableCache = new();
    // 로드된 에셋을 관리하는 캐시 딕셔너리
    private Dictionary<string, AsyncOperationHandle> _resources = new Dictionary<string, AsyncOperationHandle>();

    // 싱글톤 혹은 필요한 곳에서 호출할 수 있도록 초기화
    public void Init()
    {
        
    }

    // 1. 에셋 로드 함수
    public void LoadAsync<T>(string key, System.Action<T> callback = null) where T : UnityEngine.Object
    {
        // 이미 캐싱되어 있다면 바로 콜백 반환
        if (_resources.TryGetValue(key, out AsyncOperationHandle handle))
        {
            callback?.Invoke(handle.Result as T);
            return;
        }

        // 어드레서블 로드 실행
        var asyncOperation = Addressables.LoadAssetAsync<T>(key);
        asyncOperation.Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                _resources.Add(key, op);
                callback?.Invoke(op.Result);
            }
            else
            {
                Debug.LogError($"에셋 로드 실패: {key}");
            }
        };
    }

    // 2. 프리팹 생성 함수 (Instantiate)
    public void InstantiateAsync(string key, System.Action<GameObject> callback = null)
    {
        Addressables.InstantiateAsync(key).Completed += (op) =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded)
            {
                callback?.Invoke(op.Result);
            }
        };
    }

    // 3. 에셋 해제 (Release)
    public void Release(string key)
    {
        if (_resources.TryGetValue(key, out AsyncOperationHandle handle))
        {
            Addressables.Release(handle);
            _resources.Remove(key);
            Debug.Log($"에셋 해제 완료: {key}");
        }
    }
}

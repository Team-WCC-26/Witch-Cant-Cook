#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 컨베이어 재료를 집은 시점과 다음 재료가 화면에 생성된 시점의 간격을 기록합니다.
/// 에디터와 Development Build에서만 동작합니다.
/// </summary>
public sealed class ConveyorSpawnTimingDebug : MonoBehaviour
{
    private readonly Dictionary<CatchableObj, System.Action> pickupHandlers = new();
    private readonly HashSet<long> observedNetworkIds = new();
    private readonly Dictionary<int, float> lastPickupTimeByBelt = new();
    private readonly Dictionary<int, float> lastSpawnTimeByBelt = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        new GameObject(nameof(ConveyorSpawnTimingDebug)).AddComponent<ConveyorSpawnTimingDebug>();
    }

    private void Update()
    {
        if (ObjectPoolManager.Instance == null) return;

        foreach ((long networkId, Object activeObject) in ObjectPoolManager.Instance.activeObjDict)
        {
            if (!observedNetworkIds.Add(networkId)) continue;
            if (activeObject is not GameObject gameObject) continue;
            if (!gameObject.TryGetComponent(out CatchableObj catchable)) continue;
            if (!ConveyorBeltRegistry.TryGetOwner(networkId, out ConveyorBeltController belt)) continue;

            if (!pickupHandlers.ContainsKey(catchable))
            {
                System.Action handler = () => OnItemPicked(catchable);
                pickupHandlers.Add(catchable, handler);
                catchable.OnPicked += handler;
            }

            float now = Time.realtimeSinceStartup;
            int beltId = belt.BeltId;

            if (lastSpawnTimeByBelt.TryGetValue(beltId, out float previousSpawnTime))
            {
                Debug.Log($"[Conveyor Timing] Belt {beltId}: visual spawn interval = {now - previousSpawnTime:F3}s (entity {networkId})");
            }

            if (lastPickupTimeByBelt.Remove(beltId, out float pickupTime))
            {
                Debug.Log($"[Conveyor Timing] Belt {beltId}: picked -> next visual spawn = {now - pickupTime:F3}s (entity {networkId})");
            }

            lastSpawnTimeByBelt[beltId] = now;
        }
    }

    private void OnItemPicked(CatchableObj catchable)
    {
        if (catchable == null) return;
        if (!ConveyorBeltRegistry.TryGetOwner(catchable.NetworkId, out ConveyorBeltController belt)) return;

        float now = Time.realtimeSinceStartup;
        lastPickupTimeByBelt[belt.BeltId] = now;
        Debug.Log($"[Conveyor Timing] Belt {belt.BeltId}: picked at {now:F3}s (entity {catchable.NetworkId})");
    }

    private void OnDestroy()
    {
        foreach ((CatchableObj item, System.Action handler) in pickupHandlers)
        {
            if (item != null) item.OnPicked -= handler;
        }
    }
}
#endif

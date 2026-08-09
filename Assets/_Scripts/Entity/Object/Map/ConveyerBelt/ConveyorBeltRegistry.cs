using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 씬에 여러 개의 ConveyorBeltController가 존재할 때 필요한 전역 레지스트리.
///
/// 두 가지 역할:
/// 1) 재료 하나가 지금 "어느 벨트가 담당 중인지" 전역적으로 추적 (이중 등록 방지)
/// 2) 스폰 위치로부터 "가장 가까운 시작점을 가진 벨트"를 찾아줌 (스폰 시스템이 사용)
///
/// 각 ConveyorBeltController가 OnEnable/OnDisable에서 스스로 등록/해제한다.
/// </summary>
public static class ConveyorBeltRegistry
{
    private static readonly List<ConveyorBeltController> activeBelts = new();
    private static readonly Dictionary<long, ConveyorBeltController> itemOwner = new();

    public static void RegisterBelt(ConveyorBeltController belt)
    {
        if (!activeBelts.Contains(belt))
            activeBelts.Add(belt);
    }

    public static void UnregisterBelt(ConveyorBeltController belt)
    {
        activeBelts.Remove(belt);
    }

    /// <summary>
    /// worldPos(보통 스폰 지점 위치)에서 가장 가까운 시작점(waypoints[0])을 가진 벨트를 찾는다.
    /// maxDistance 안에 아무 벨트도 없으면 null 반환.
    /// </summary>
    public static ConveyorBeltController FindBeltNearStart(Vector3 worldPos, float maxDistance = 2f)
    {
        ConveyorBeltController best = null;
        float bestSqr = maxDistance * maxDistance;

        foreach (var belt in activeBelts)
        {
            if (belt == null || belt.Path == null) continue;
            var waypoints = belt.Path.Waypoints;
            if (waypoints == null || waypoints.Length == 0 || waypoints[0] == null) continue;

            float sqr = (waypoints[0].position - worldPos).sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = belt;
            }
        }

        return best;
    }

    // ---- 아이템 소유권 ----

    public static void SetOwner(long itemId, ConveyorBeltController belt)
    {
        itemOwner[itemId] = belt;
    }

    public static bool TryGetOwner(long itemId, out ConveyorBeltController belt)
        => itemOwner.TryGetValue(itemId, out belt);

    public static bool IsOwnedByAnyBelt(long itemId) => itemOwner.ContainsKey(itemId);

    /// <summary>belt가 실제로 그 아이템의 현재 소유자일 때만 제거 (다른 벨트가 이미 가져간 경우 덮어쓰지 않도록).</summary>
    public static void ClearOwner(long itemId, ConveyorBeltController belt)
    {
        if (itemOwner.TryGetValue(itemId, out var current) && current == belt)
            itemOwner.Remove(itemId);
    }
}
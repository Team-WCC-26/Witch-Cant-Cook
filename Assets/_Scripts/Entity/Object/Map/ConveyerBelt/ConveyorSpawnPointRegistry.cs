using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ConveyorSpawnPoint들을 beltId(=서버 패킷의 ConveyId)로 조회할 수 있게 해주는 레지스트리.
/// ConveyorBeltRegistry와 대칭 구조. 서버가 위치 정보 없이 ConveyId만 보내주므로,
/// 실제 스폰 좌표는 이 레지스트리를 통해 씬에서 찾아야 한다.
/// </summary>
public static class ConveyorSpawnPointRegistry
{
    private static readonly Dictionary<int, ConveyorSpawnPoint> pointsById = new();

    public static void Register(ConveyorSpawnPoint point)
    {
        if (pointsById.TryGetValue(point.BeltId, out var existing) && existing != point)
        {
            Debug.LogWarning($"[ConveyorSpawnPointRegistry] beltId {point.BeltId} 중복 등록 감지. " +
                              $"기존: {existing.name}, 새로: {point.name}. ID를 확인하세요.");
        }
        pointsById[point.BeltId] = point;
    }

    public static void Unregister(ConveyorSpawnPoint point)
    {
        if (pointsById.TryGetValue(point.BeltId, out var current) && current == point)
            pointsById.Remove(point.BeltId);
    }

    public static bool TryGetSpawnPoint(int beltId, out ConveyorSpawnPoint point)
        => pointsById.TryGetValue(beltId, out point);
}
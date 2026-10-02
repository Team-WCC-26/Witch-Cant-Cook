using Protocol;
using UnityEngine;

/// <summary>서버가 확정한 제출 성공에만 제출대에서 효과를 재생합니다.</summary>
public static class DishSubmissionVfx
{
    private const string ResourcePath = "VFX/DishSubmissionSuccess";
    private const float HeightOffset = 0.25f;
    private static DishCompleteVfx successPrefab;

    // 주문 실패 알림은 시간 초과일 수도 있으므로 제출 실패 효과로 사용하지 않습니다.
    public static bool PlayForServerResult(DishState state)
    {
        if (state != DishState.Success) return false;

        SubmitTriggerZone zone = Object.FindFirstObjectByType<SubmitTriggerZone>();
        if (zone == null) return false;

        if (successPrefab == null)
            successPrefab = Resources.Load<DishCompleteVfx>(ResourcePath);
        if (successPrefab == null)
        {
            Debug.LogWarning("제출 성공 파티클 프리팹을 찾을 수 없습니다.", zone);
            return false;
        }

        // 기존 성공 패킷에는 접시 번호와 위치가 없으므로 공통 제출대 위치를 사용합니다.
        Collider trigger = zone.GetComponent<Collider>();
        Vector3 position = trigger != null ? trigger.bounds.center : zone.transform.position;
        position += Vector3.up * HeightOffset;

        DishCompleteVfx effect = Object.Instantiate(successPrefab, position, Quaternion.identity);
        effect.name = "요리제출_성공_파티클";
        effect.PlayOnce();
        return true;
    }
}

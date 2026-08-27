using UnityEngine;

/// <summary>
/// 벨트 표면에 얇게 배치하는 트리거. 드롭/스로우된 재료가 실제로 벨트 표면에
/// "착지"하면 감지해서 ConveyorBeltController에 재등록한다.
///
/// 배치 방법: 벨트 파츠(직선/코너) 상판 바로 위, 아주 얇은(예: 두께 2~5cm) Box/Mesh
/// 트리거 콜라이더로 벨트 전체 표면을 덮도록 만든다. 허공에 크게 띄운 트리거가 아니라
/// 표면에 밀착시키는 이유는, 재료가 벨트 위를 던져져 지나가기만 하는 경우(아직 착지 전)와
/// 실제로 벨트에 얹혀 정지/이동하는 경우를 구분하기 위함이다.
///
/// 여러 벨트 파츠에 나눠 붙여도 되고, 벨트 전체를 덮는 트리거 하나로 만들어도 된다.
/// 어느 쪽이든 같은 ConveyorBeltController를 참조하면 된다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ConveyorReentryTrigger : MonoBehaviour
{
    [SerializeField] private ConveyorBeltController belt;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (belt == null) return;
        if (!other.TryGetComponent(out CatchableObj catchable)) return;

        // 잡혀있는 동안은 콜라이더 자체가 꺼져있어(SetPhysicsState(false)) 정상적으로는
        // 여기 안 들어오지만, 방어적으로 한 번 더 체크
        if (catchable.IsHold) return;

        long networkId = catchable.NetworkId;

        // 이 벨트뿐 아니라 다른 벨트가 이미 담당 중인 경우도 걸러내야 함
        if (ConveyorBeltRegistry.IsOwnedByAnyBelt(networkId)) return;

        float distance = belt.Path.GetNearestDistance(other.transform.position);
        belt.RegisterItemAtDistance(networkId, other.transform, other.gameObject, distance);

        Debug.Log($"[ConveyorReentryTrigger] Re-registered dropped item. NetworkID: {networkId}, distance: {distance:F2}");
    }
}
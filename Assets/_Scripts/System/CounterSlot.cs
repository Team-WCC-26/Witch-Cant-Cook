using UnityEngine;

/// <summary>
/// 조리대 슬롯의 데이터와 배치를 관리하는 클래스
/// </summary>
public class CounterSlot : MonoBehaviour
{
    [Header("슬롯 상태")]
    public bool isOccupied = false;
    public GameObject attachedObject;

    [Header("배치 설정")]
    [SerializeField] private Transform snapPoint; // 물건이 위치할 정확한 지점

    // 인스펙터에서 snapPoint를 설정하지 않았을 경우를 대비한 방어 코드
    private void Awake()
    {
        if (snapPoint == null) snapPoint = this.transform;
    }

    /// <summary>
    /// 플레이어가 물건을 조리대에 놓을 때 호출
    /// </summary>
    public void PlaceObject(GameObject obj)
    {
        if (isOccupied) return;

        isOccupied = true;
        attachedObject = obj;

        // 1. 물리/부모 관계 설정
        obj.transform.SetParent(snapPoint);

        // 2. 위치 및 회전값 초기화 (Snap)
        obj.transform.localPosition = Vector3.zero;
        obj.transform.localRotation = Quaternion.identity;

        // TODO: ECS 시스템에 "조리 시작" 신호를 보내는 로직
        Debug.Log($"[CookingStation] {obj.name} 배치 완료");
    }

    /// <summary>
    /// 플레이어가 조리대에서 물건을 집어갈 때 호출
    /// </summary>
    /// <returns>슬롯에 있던 오브젝트 반환</returns>
    public GameObject TakeObject()
    {
        if (!isOccupied || attachedObject == null) return null;

        GameObject objToReturn = attachedObject;

        // 1. 상태 초기화
        isOccupied = false;
        attachedObject = null;

        // TODO: ECS 시스템에 "조리 중단" 신호를 보내는 로직
        Debug.Log($"[CookingStation] {objToReturn.name} 제거됨");

        return objToReturn;
    }
}
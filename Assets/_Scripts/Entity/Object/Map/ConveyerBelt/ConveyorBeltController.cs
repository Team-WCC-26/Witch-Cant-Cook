using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 벨트 위 재료 하나를 이동시키는 데 필요한 최소 상태.
/// 재료의 게임플레이 속성(무게, 타입, 폭발 여부 등)과는 무관하며,
/// 오직 "이 Transform을 언제부터 얼마나 움직일지"만 담는다.
/// </summary>
public class ConveyorItemState
{
    public long ServerItemId;
    public Transform ItemTransform;
    public GameObject GameObject;
    public CatchableObj Catchable; // 잡힘 여부(IsHold) 확인용, 없으면 null 허용
    public float LocalSpawnTime;   // 클라이언트가 스폰 이벤트를 수신한 로컬 Time.time
    public bool IsMovingOnBelt;

    // 착지 지점이 벨트 중심선에서 좌우로 벗어난 정도. 이동 내내 유지되어
    // 모든 재료가 하나의 레일 위를 도는 것처럼 보이지 않게 한다.
    public float LateralOffset;

    // 등록 직후 실제 위치 -> 경로 목표 위치로 짧게 보간(settle)하기 위한 상태
    public bool IsSettling;
    public float SettleStartTime;
    public Vector3 SettleStartPosition;
    public Quaternion SettleStartRotation;
}

/// <summary>
/// 벨트 하나를 표현하는 컨트롤러.
/// - 벨트 위에 있는 재료들의 ConveyorItemState 리스트를 직접 들고 있다가
///   매 프레임 한 번의 루프로 전부 이동시킨다 (재료 프리팹에는 아무 컴포넌트도 붙이지 않음).
/// - 위치 동기화 패킷 없이, 스폰 시각 + 속도로 로컬 계산만 한다 (오차 허용).
/// - 벨트 끝 도달 판정은 이 스크립트가 하지 않는다. 별도의 ConveyorEndTrigger가
///   담당하고, 감지되면 UnregisterItem()을 호출해 이 리스트에서만 빠진다.
/// </summary>
public class ConveyorBeltController : MonoBehaviour
{
    [SerializeField] private int beltId; // DB의 beltID와 동일한 값을 인스펙터에서 입력
    [SerializeField] private ConveyorPath path;
    [SerializeField] private float beltSpeed = 2f;
    [SerializeField] private Renderer beltRenderer;
    [SerializeField] private float rotationLerpSpeed = 10f;
    [SerializeField] private float maxLateralOffset = 0.35f; // 벨트 폭 절반 정도로, 실제 벨트 폭에 맞춰 조정
    [SerializeField] private float settleDuration = 0.2f;    // 착지 순간 -> 경로 추종 시작까지 보간 시간

    public int BeltId => beltId;
    public ConveyorPath Path => path;
    public float Speed => beltSpeed;

    private readonly List<ConveyorItemState> itemsOnBelt = new();
    private readonly Dictionary<long, ConveyorItemState> itemLookup = new();

    private void OnEnable()
    {
        ConveyorBeltRegistry.RegisterBelt(this);
    }

    private void OnDisable()
    {
        ConveyorBeltRegistry.UnregisterBelt(this);
    }

    /// <summary>
    /// 스폰 시점에 IngredientSpawnSystem 등에서 호출.
    /// 재료 프리팹의 Transform만 넘겨받아 이동을 대신 맡는다.
    /// 착지 위치를 경로에 딱 맞추지 않고, 그 위치와 경로 사이의 실제 관계를 계산해서
    /// (좌우 오프셋 + 정착 보간) 자연스럽게 흡수되도록 한다.
    /// </summary>
    public void RegisterItem(long serverItemId, Transform itemTransform, GameObject itemObject)
    {
        AddItemInternal(serverItemId, itemTransform, itemObject, explicitDistance: null);
    }

    /// <summary>
    /// 드롭 후 벨트 위에 다시 착지한 재료를 등록할 때 사용. distance는 보통
    /// ConveyorReentryTrigger가 path.GetNearestDistance()로 미리 계산해서 넘겨준다.
    /// </summary>
    public void RegisterItemAtDistance(long serverItemId, Transform itemTransform, GameObject itemObject, float distance)
    {
        AddItemInternal(serverItemId, itemTransform, itemObject, explicitDistance: distance);
    }

    private void AddItemInternal(long serverItemId, Transform itemTransform, GameObject itemObject, float? explicitDistance)
    {
        if (ConveyorBeltRegistry.IsOwnedByAnyBelt(serverItemId)) return; // 다른 벨트가 이미 담당 중이면 무시

        itemObject.TryGetComponent(out CatchableObj catchable);

        Vector3 currentPos = itemTransform.position;
        // 명시적 distance가 없으면(=스폰 직후) 지금 위치와 가장 가까운 경로 지점을 역산
        float distance = explicitDistance ?? path.GetNearestDistance(currentPos);
        var (pathPoint, tangent) = path.Evaluate(distance);

        // 착지 지점이 벨트 중심선에서 좌우로 얼마나 벗어났는지 계산 (수평 폭 방향 기준)
        Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
        float lateral = Vector3.Dot(currentPos - pathPoint, right);
        lateral = Mathf.Clamp(lateral, -maxLateralOffset, maxLateralOffset);

        float distanceTravelTime = beltSpeed > 0.0001f ? distance / beltSpeed : 0f;

        var state = new ConveyorItemState
        {
            ServerItemId = serverItemId,
            ItemTransform = itemTransform,
            GameObject = itemObject,
            Catchable = catchable,
            LocalSpawnTime = Time.time - distanceTravelTime, // 착지 지점부터 이어서 흐르도록 역산
            IsMovingOnBelt = true,
            LateralOffset = lateral,
            IsSettling = settleDuration > 0f,
            SettleStartTime = Time.time,
            SettleStartPosition = currentPos,
            SettleStartRotation = itemTransform.rotation
        };

        itemsOnBelt.Add(state);
        itemLookup[serverItemId] = state;
        ConveyorBeltRegistry.SetOwner(serverItemId, this);
    }

    /// <summary>
    /// 벨트 끝 트리거, 폭발 이벤트 등 "더 이상 이 벨트가 이동을 책임지지 않아도 될 때" 호출.
    /// 리스트/딕셔너리에서만 제거하며, 오브젝트 파괴나 풀 반환은 호출자 책임.
    /// </summary>
    public void UnregisterItem(long serverItemId)
    {
        if (itemLookup.TryGetValue(serverItemId, out var state))
        {
            state.IsMovingOnBelt = false;
            itemsOnBelt.Remove(state);
            itemLookup.Remove(serverItemId);
            ConveyorBeltRegistry.ClearOwner(serverItemId, this);
        }
    }

    public bool TryGetItem(long serverItemId, out ConveyorItemState state)
        => itemLookup.TryGetValue(serverItemId, out state);

    public bool IsRegistered(long serverItemId) => itemLookup.ContainsKey(serverItemId);

    void Update()
    {
        MoveItemsOnBelt();
        //ScrollBeltTexture();
    }

    private void MoveItemsOnBelt()
    {
        if (path == null || path.TotalLength <= 0f) return;

        // 뒤에서부터 순회: 도달 처리 중 리스트에서 제거해도 인덱스가 안 꼬임
        for (int i = itemsOnBelt.Count - 1; i >= 0; i--)
        {
            var item = itemsOnBelt[i];
            if (!item.IsMovingOnBelt || item.ItemTransform == null) continue;

            // 플레이어가 잡은 순간(IsHold == true) 벨트 관리에서 빠진다.
            // CatchableObj.OnPick()이 호출되면 IsHold가 true가 되는데, 그 시점부터는
            // PlayerInteract.ApplyPicked()가 transform.parent를 손으로 바꿔주므로
            // 벨트가 계속 transform.position을 덮어쓰면 손에서 위치가 튐/안 붙는 문제가 생긴다.
            if (item.Catchable != null && item.Catchable.IsHold)
            {
                itemsOnBelt.RemoveAt(i);
                itemLookup.Remove(item.ServerItemId);
                ConveyorBeltRegistry.ClearOwner(item.ServerItemId, this);
                continue;
            }

            float elapsed = Time.time - item.LocalSpawnTime;
            float distance = elapsed * beltSpeed;

            // 벨트 끝을 넘어서면 트리거가 놓친 경우에 대비한 안전장치.
            // 정상 흐름에서는 ConveyorEndTrigger의 콜라이더가 먼저 UnregisterItem을 호출한다.
            if (distance >= path.TotalLength)
            {
                distance = path.TotalLength;
            }

            var (point, tangent) = path.Evaluate(distance);
            Quaternion targetRot = tangent.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(tangent, Vector3.up)
                : item.ItemTransform.rotation;

            // 등록 시점에 계산해둔 좌우 오프셋을 유지한 채 경로를 따라간다.
            // (모든 재료가 하나의 중심선 위를 그대로 지나가지 않도록)
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            Vector3 targetPos = point + right * item.LateralOffset;

            if (item.IsSettling)
            {
                float t = settleDuration > 0f ? (Time.time - item.SettleStartTime) / settleDuration : 1f;

                if (t >= 1f)
                {
                    item.IsSettling = false;
                    item.ItemTransform.position = targetPos;
                    item.ItemTransform.rotation = targetRot;
                }
                else
                {
                    // 착지 시점 실제 위치/회전에서 경로 추종 목표로 부드럽게 흡수
                    item.ItemTransform.position = Vector3.Lerp(item.SettleStartPosition, targetPos, t);
                    item.ItemTransform.rotation = Quaternion.Slerp(item.SettleStartRotation, targetRot, t);
                }
            }
            else
            {
                item.ItemTransform.position = targetPos;
                item.ItemTransform.rotation = Quaternion.Slerp(
                    item.ItemTransform.rotation, targetRot, Time.deltaTime * rotationLerpSpeed);
            }
        }
    }

    private void ScrollBeltTexture()
    {
        if (beltRenderer == null) return;

        // UV 스크롤은 순수 로컬 비주얼, 동기화 불필요
        float offset = Time.time * beltSpeed;
        beltRenderer.material.SetTextureOffset("_MainTex", new Vector2(0, offset));
    }
}
using UnityEngine;

/// <summary>
/// 공중에서 목표 높이(플레이어 머리 정도)를 유지하며 배회하고,
/// 벽이나 다른 오브젝트에 가까워지면 방향을 전환하는 재료 속성.
///
/// 스폰 후 비행을 시작하기까지의 대기 시간은 이 트레잇이 아니라
/// IngredientNalchi이 자체 IngredientTraitTimer로 관리한다.
/// 대기가 끝나면 IngredientNalchi이 StartFlying()을 호출해 비행을 시작시킨다.
/// </summary>
public class IngredientTraitFly : IngredientTrait
{
    [Header("Height")]
    [Tooltip("비행 시작 시점(스폰 위치, 바닥 기준)으로부터 띄울 높이(플레이어 머리 정도).")]
    [SerializeField] private float targetHeight = 2f;
    [Tooltip("목표 높이로 보간되는 속도. 클수록 빠르게 높이를 맞춘다.")]
    [SerializeField] private float heightFollowSpeed = 3f;

    [Header("Wander")]
    [SerializeField] private float flySpeed = 3f;
    [Tooltip("방향을 바꿀 때 회전이 부드럽게 이어지는 속도(도/초).")]
    [SerializeField] private float turnSpeed = 90f;
    [Tooltip("몇 초마다 배회 방향을 랜덤하게 조금씩 바꿀지.")]
    [SerializeField] private float directionChangeInterval = 3f;
    [Tooltip("한 번에 방향을 바꿀 때의 최대 각도 범위.")]
    [SerializeField] private float directionChangeRandomRange = 60f;

    [Header("Obstacle Avoidance")]
    [Tooltip("이 거리 안에 장애물이 있으면 방향을 전환한다.")]
    [SerializeField] private float obstacleDetectRange = 2f;
    [SerializeField] private float obstacleCheckRadius = 0.3f;
    [Tooltip("Small local avoidance box used only for other flying fish. Map obstacles use the full physical bounds.")]
    [SerializeField] private bool useCustomAvoidanceBounds;
    [SerializeField] private Vector3 avoidanceCenter;
    [SerializeField] private Vector3 avoidanceSize = Vector3.one * 0.6f;
    [Tooltip("Extra clearance in world meters around the full physical bounds when avoiding map obstacles.")]
    [SerializeField, Min(0f)] private float environmentClearance = 0.15f;
    [Tooltip("추가로 감지할 장애물 레이어. Default, Floor, Wall, Interactable은 항상 감지합니다.")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;

    private readonly IngredientTraitTimer wanderTimer = new();

    private Rigidbody rb;
    private CatchableObj catchable;
    private Collider[] bodyColliders;
    private readonly RaycastHit[] obstacleHits = new RaycastHit[32];
    private bool gravityBeforeFlight;
    private bool isFlying;
    private Vector3 flyDirection;
    private float cachedDesiredY;

    public bool IsFlying => isFlying;

    private void Awake()
    {
        TryGetComponent(out rb);
        TryGetComponent(out catchable);
        bodyColliders = GetComponentsInChildren<Collider>(true);
        flyDirection = transform.forward;
    }

    protected override void StopTrait()
    {
        StopFlying();
    }

    private void Update()
    {
        if (!isFlying)
            return;

        wanderTimer.Tick(Time.deltaTime);
    }

    private void FixedUpdate()
    {
        if (!isFlying)
            return;

        AvoidObstacles(Time.fixedDeltaTime);
        Fly(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 비행을 시작한다. IngredientNalchi이 스폰 대기 타이머 종료 시 호출한다.
    /// </summary>
    public void StartFlying()
    {
        if (!IsTraitAvailable || isFlying || (catchable != null && catchable.IsHold))
            return;

        // Conveyor updates must stop owning this transform before flight starts moving it.
        if (catchable != null && ConveyorBeltRegistry.TryGetOwner(catchable.NetworkId, out ConveyorBeltController belt) && belt != null)
            belt.UnregisterItem(catchable.NetworkId);
        if (rb != null)
        {
            gravityBeforeFlight = rb.useGravity;
            // A dynamic body lets the physics solver stop against stairs, walls and ceilings.
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }
        isFlying = true;

        // 평지 맵 기준: 비행을 시작하는 시점의 위치(스폰 시 바닥 위)를 기준으로
        // targetHeight만큼 띄운 고도를 한 번만 계산해서 캐싱한다.
        // 이후로는 레이캐스트 없이 이 고정된 값으로 계속 수렴시킨다.
        cachedDesiredY = transform.position.y + targetHeight;

        PickRandomDirection();
        wanderTimer.StartLoop(directionChangeInterval, PickRandomDirection);
    }

    /// <summary>
    /// 비행을 멈춘다. 예: 플레이어가 재료를 집었을 때 IngredientNalchi에서 호출.
    /// </summary>
    public void StopFlying()
    {
        if (isFlying && rb != null)
        {
            rb.useGravity = gravityBeforeFlight;
            CatchableObj catchable = GetComponent<CatchableObj>();
            if (catchable == null || !catchable.IsHold)
            {
                rb.isKinematic = false;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
        isFlying = false;
        wanderTimer.Stop();
    }

    private void PickRandomDirection()
    {
        float randomAngle = Random.Range(-directionChangeRandomRange, directionChangeRandomRange);
        Vector3 newDir = Quaternion.Euler(0f, randomAngle, 0f) * flyDirection;
        newDir.y = 0f;
        flyDirection = newDir.normalized;
    }

    private void AvoidObstacles(float deltaTime)
    {
        Vector3 velocity = GetFlightVelocity(deltaTime);
        if (velocity.sqrMagnitude < 0.0001f) return;
        Vector3 direction = velocity.normalized;
        float distance = Mathf.Max(obstacleDetectRange, velocity.magnitude * deltaTime + 0.05f);
        int mask = obstacleLayerMask.value | LayerMask.GetMask("Default", "Floor", "Wall", "Interactable");
        float nearest = float.PositiveInfinity;
        Vector3 normal = Vector3.zero;

        // Walls and props must clear both the body and wings, plus a world-space margin.
        Bounds physicalBounds = GetBodyBounds();
        FindObstacle(physicalBounds, Quaternion.identity, Mathf.Max(0f, environmentClearance),
            direction, distance, mask, false, ref nearest, ref normal);

        // Fish keep the independent, smaller sensing volume.
        Bounds fishBounds = useCustomAvoidanceBounds ? GetFishAvoidanceBounds() : physicalBounds;
        FindObstacle(fishBounds, useCustomAvoidanceBounds ? transform.rotation : Quaternion.identity,
            -0.01f, direction, distance, mask, true, ref nearest, ref normal);
        if (float.IsPositiveInfinity(nearest)) return;

        // Stop trying to climb into a ceiling or descend through a surface.
        float currentY = rb != null ? rb.position.y : transform.position.y;
        if (normal.y < -0.5f && velocity.y > 0f)
            cachedDesiredY = Mathf.Min(cachedDesiredY, currentY);
        else if (normal.y > 0.5f && velocity.y < 0f)
            cachedDesiredY = Mathf.Max(cachedDesiredY, currentY);

        Vector3 reflected = Vector3.Reflect(flyDirection, normal);
        reflected.y = 0f;
        if (reflected.sqrMagnitude < 0.0001f || Vector3.Dot(reflected.normalized, flyDirection) > 0.99f)
            reflected = -flyDirection;
        flyDirection = reflected.normalized;
    }

    private void FindObstacle(Bounds bounds, Quaternion orientation, float padding, Vector3 direction,
        float distance, int mask, bool fishOnly, ref float nearest, ref Vector3 normal)
    {
        Vector3 extents = Vector3.Max(bounds.extents + Vector3.one * padding, Vector3.one * 0.02f);
        int count = Physics.BoxCastNonAlloc(bounds.center, extents, direction, obstacleHits,
            orientation, distance, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = obstacleHits[i];
            Collider collider = hit.collider;
            if (collider == null || collider.attachedRigidbody == rb || collider.transform.IsChildOf(transform)) continue;
            if ((collider.GetComponentInParent<IngredientNalchi>() != null) != fishOnly) continue;

            Vector3 hitNormal = hit.normal;
            if (hit.distance <= 0.0001f)
            {
                // An expanded box can already overlap the floor or a nearby wall.
                // Casts report -direction for such overlaps; use the surface direction
                // so moving away from the floor/wall does not trigger a reversal.
                // ClosestPoint does not support non-convex map meshes.
                bool concaveMesh = collider is MeshCollider mesh && !mesh.convex;
                Vector3 point = concaveMesh
                    ? collider.ClosestPointOnBounds(bounds.center)
                    : collider.ClosestPoint(bounds.center);
                Vector3 separation = bounds.center - point;
                if (separation.sqrMagnitude > 0.000001f)
                    hitNormal = separation.normalized;
                else if (concaveMesh)
                {
                    // Combined room meshes can enclose the query in their AABB.
                    // A real surface ahead is needed before reversing flight.
                    if (!collider.Raycast(new Ray(bounds.center, direction), out RaycastHit surface,
                        extents.magnitude)) continue;
                    hitNormal = surface.normal;
                }
            }
            if (Vector3.Dot(direction, hitNormal) >= -0.001f || hit.distance >= nearest) continue;
            nearest = hit.distance;
            normal = hitNormal;
        }
    }

    private Bounds GetFishAvoidanceBounds()
    {
        Vector3 scale = transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        return new Bounds(transform.TransformPoint(avoidanceCenter),
            Vector3.Scale(Vector3.Max(avoidanceSize, Vector3.one * 0.01f), scale));
    }

    private Bounds GetBodyBounds()
    {
        Bounds bounds = new(transform.position, Vector3.one * Mathf.Max(0.02f, obstacleCheckRadius) * 2f);
        bool found = false;
        foreach (Collider collider in bodyColliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || collider.attachedRigidbody != rb) continue;
            if (!found) { bounds = collider.bounds; found = true; }
            else bounds.Encapsulate(collider.bounds);
        }
        return bounds;
    }

    private Vector3 GetFlightVelocity(float deltaTime)
    {
        Vector3 position = rb != null ? rb.position : transform.position;
        float newY = Mathf.Lerp(position.y, cachedDesiredY, heightFollowSpeed * deltaTime);
        return flyDirection * flySpeed + Vector3.up * ((newY - position.y) / Mathf.Max(deltaTime, 0.0001f));
    }

    private void Fly(float deltaTime)
    {
        if (rb == null || rb.isKinematic) return;
        Quaternion targetRot = Quaternion.LookRotation(flyDirection, Vector3.up);
        rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, turnSpeed * deltaTime));
        rb.linearVelocity = GetFlightVelocity(deltaTime);
    }
}

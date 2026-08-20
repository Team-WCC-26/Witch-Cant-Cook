using UnityEngine;

/// <summary>
/// 공중에서 목표 높이(플레이어 머리 정도)를 유지하며 배회하고,
/// 벽이나 다른 오브젝트에 가까워지면 방향을 전환하는 재료 속성.
///
/// 스폰 후 비행을 시작하기까지의 대기 시간은 이 트레잇이 아니라
/// IngredientSalmon이 자체 IngredientTraitTimer로 관리한다.
/// 대기가 끝나면 IngredientSalmon이 StartFlying()을 호출해 비행을 시작시킨다.
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
    [Tooltip("벽/장애물로 판정할 레이어. 바닥 레이어와는 분리해서 설정 권장.")]
    [SerializeField] private LayerMask obstacleLayerMask = ~0;

    private readonly IngredientTraitTimer wanderTimer = new();

    private Rigidbody rb;
    private bool isFlying;
    private Vector3 flyDirection;
    private float cachedDesiredY;

    public bool IsFlying => isFlying;

    private void Awake()
    {
        TryGetComponent(out rb);
        flyDirection = transform.forward;
    }

    private void OnDisable()
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

        AvoidObstacles();
        Fly(Time.fixedDeltaTime);
    }

    /// <summary>
    /// 비행을 시작한다. IngredientSalmon이 스폰 대기 타이머 종료 시 호출한다.
    /// </summary>
    public void StartFlying()
    {
        if (isFlying)
            return;

        isFlying = true;

        // 평지 맵 기준: 비행을 시작하는 시점의 위치(스폰 시 바닥 위)를 기준으로
        // targetHeight만큼 띄운 고도를 한 번만 계산해서 캐싱한다.
        // 이후로는 레이캐스트 없이 이 고정된 값으로 계속 수렴시킨다.
        cachedDesiredY = transform.position.y + targetHeight;

        PickRandomDirection();
        wanderTimer.StartLoop(directionChangeInterval, PickRandomDirection);
    }

    /// <summary>
    /// 비행을 멈춘다. 예: 플레이어가 재료를 집었을 때 IngredientSalmon에서 호출.
    /// </summary>
    public void StopFlying()
    {
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

    private void AvoidObstacles()
    {
        Vector3 origin = transform.position;

        bool hitSomething = Physics.SphereCast(
            origin,
            obstacleCheckRadius,
            flyDirection,
            out RaycastHit hit,
            obstacleDetectRange,
            obstacleLayerMask,
            QueryTriggerInteraction.Ignore
        );

        if (!hitSomething)
            return;

        // 장애물 표면 노멀을 기준으로 반사시켜 방향을 바꾼다.
        Vector3 reflected = Vector3.Reflect(flyDirection, hit.normal);
        reflected.y = 0f;
        reflected.Normalize();

        // 완전히 반사만 시키면 왔던 경로를 그대로 오갈 수 있어 약간의 랜덤성을 섞는다.
        float randomAngle = Random.Range(-30f, 30f);
        flyDirection = (Quaternion.Euler(0f, randomAngle, 0f) * reflected).normalized;
    }

    private void Fly(float deltaTime)
    {
        Quaternion targetRot = Quaternion.LookRotation(flyDirection, Vector3.up);
        Quaternion newRot = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * deltaTime);

        float newY = Mathf.Lerp(transform.position.y, cachedDesiredY, heightFollowSpeed * deltaTime);

        Vector3 horizontalMove = flyDirection * (flySpeed * deltaTime);
        Vector3 newPosition = new Vector3(
            transform.position.x + horizontalMove.x,
            newY,
            transform.position.z + horizontalMove.z
        );

        if (rb != null && rb.isKinematic)
        {
            rb.MoveRotation(newRot);
            rb.MovePosition(newPosition);
        }
        else
        {
            transform.rotation = newRot;
            transform.position = newPosition;
        }
    }
}
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
[RequireComponent(typeof(IngredientTraitFly))]
[RequireComponent(typeof(Rigidbody))]
public class IngredientNalchi : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;
    [SerializeField] private IngredientTraitFly flyTrait;
    [SerializeField] private Rigidbody rb;

    [Header("Fly Timing")]
    [Tooltip("스폰 후(또는 내려놓은 후) 비행을 시작하기까지 대기 시간.")]
    [SerializeField] private float delayBeforeFly = 2f;

    [Header("Stun")]
    [Tooltip("물리적 충돌을 받았을 때 기절하는 시간.")]
    [SerializeField] private float stunDuration = 10f;
    [Tooltip("이 속도(m/s) 이상의 충돌만 기절로 처리한다. 낮으면 그냥 내려놓고 착지하는 것도 기절로 잡힌다.")]
    [SerializeField] private float minStunImpactSpeed = 3f;

    private readonly IngredientTraitTimer flyDelayTimer = new();
    private readonly IngredientTraitTimer stunTimer = new();

    private bool isStunned;
    // Spawned/wandering fish, including one stunned during flight, require a player pickup before pot intake.
    public bool IsAutonomous { get; private set; }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();

        if (flyTrait == null)
            flyTrait = GetComponent<IngredientTraitFly>();

        if (rb == null)
            rb = GetComponent<Rigidbody>();
    }

    protected override void StartTrait()
    {
        catchable.OnPicked += OnPicked;
        catchable.OnDropped += OnDropped;

        isStunned = false;
        IsAutonomous = true;
        flyTrait.StopFlying();
        BeginFlyDelay();
    }

    protected override void StopTrait()
    {
        catchable.OnPicked -= OnPicked;
        catchable.OnDropped -= OnDropped;

        flyDelayTimer.Stop();
        stunTimer.Stop();
        flyTrait.StopFlying();
        IsAutonomous = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        flyDelayTimer.Tick(dt);
        stunTimer.Tick(dt);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsTraitAvailable || isStunned)
            return;

        // 살짝 내려놓고 착지하는 정도의 약한 충돌은 무시하고,
        // 일정 속도 이상으로 부딪혔을 때만 기절 처리한다.
        if (collision.relativeVelocity.magnitude < minStunImpactSpeed)
            return;

        EnterStun();
    }

    #region Fly
    private void BeginFlyDelay()
    {
        flyDelayTimer.StartWait(delayBeforeFly, BeginFly);
    }

    private void BeginFly()
    {
        if (!IsTraitAvailable || isStunned || catchable.IsHold)
            return;

        // The flight trait owns its physics and releases conveyor movement when takeoff succeeds.
        flyTrait.StartFlying();
        if (flyTrait.IsFlying) IsAutonomous = true;
    }
    #endregion

    #region Stun
    private void EnterStun()
    {
        isStunned = true;

        flyDelayTimer.Stop();
        flyTrait.StopFlying();

        // kinematic을 풀어 중력이 작용하게 해서 바닥으로 떨어지도록 한다.
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        stunTimer.StartTimer(stunDuration, RecoverFromStun);
    }

    private void RecoverFromStun()
    {
        isStunned = false;

        // 다시 대기 후 비행을 재개한다. 기절 후 계속 바닥에 두고 싶다면 이 줄을 지우면 된다.
        BeginFlyDelay();
    }
    #endregion

    #region Catchable
    private void OnPicked()
    {
        IsAutonomous = false;
        // 들고 있는 동안에는 비행/대기/기절 상태가 전부 의미 없으므로 모두 정지시킨다.
        flyDelayTimer.Stop();
        stunTimer.Stop();
        isStunned = false;

        flyTrait.StopFlying();
    }

    private void OnDropped()
    {
        IsAutonomous = false;
        // 손에서 놓으면 물리(중력)가 적용되도록 kinematic을 해제한다.
        // 이걸 안 해주면 CatchableObj가 들고 있는 동안 켜둔 kinematic이 그대로 남아
        // 놓은 자리에 그대로 박제된 것처럼 보인다.
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // 내려놓으면 스폰 직후와 동일하게 대기 후 다시 비행을 시작한다.
        BeginFlyDelay();
    }
    #endregion
}

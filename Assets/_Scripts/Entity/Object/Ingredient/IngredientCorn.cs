using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientCorn : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Time Settings")]
    [SerializeField] private float initialWaitDuration = 2f;
    [SerializeField] private float holdDuration = 3f;
    [SerializeField] private float abandonDuration = 10f;

    private IngredientTraitExplode explodeTrait;

    private readonly IngredientTraitTimer waitTimer = new();
    private readonly IngredientTraitTimer holdTimer = new();
    private readonly IngredientTraitTimer abandonTimer = new();

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
        explodeTrait = GetComponent<IngredientTraitExplode>();
    }

    private void OnEnable()
    {
        catchable.OnPicked += OnPicked;
        catchable.OnDropped += OnDropped;

        // 활성화 직후에는 hold/abandon을 바로 시작하지 않고
        // 초기 대기 시간부터 시작한다.
        StartInitialWait();
    }

    private void OnDisable()
    {
        catchable.OnPicked -= OnPicked;
        catchable.OnDropped -= OnDropped;
        waitTimer.Stop();
        holdTimer.Stop();
        abandonTimer.Stop();
        PushIngredientToPool(catchable);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        waitTimer.Tick(dt);
        holdTimer.Tick(dt);
        abandonTimer.Tick(dt);
    }

    private void OnPicked()
    {
        // 대기 중이었다면 대기를 취소하고 바로 hold로 전환
        waitTimer.Stop();
        abandonTimer.Stop();
        StartHoldTimer();
    }

    private void OnDropped()
    {
        // 대기 중이었다면 대기를 취소하고 바로 abandon으로 전환
        waitTimer.Stop();
        holdTimer.Stop();
        StartAbandonTimer();
    }

    private void StartInitialWait()
    {
        waitTimer.StartWait(initialWaitDuration, OnInitialWaitComplete);
    }

    private void OnInitialWaitComplete()
    {
        // 대기가 끝난 시점의 상태를 기준으로 hold/abandon 타이머 시작
        if (catchable.IsHold)
        {
            StartHoldTimer();
        }
        else
        {
            StartAbandonTimer();
        }
    }

    private void StartHoldTimer()
    {
        holdTimer.StartTimer(
            holdDuration,
            TriggerCornEffect
        );
    }

    private void StartAbandonTimer()
    {
        abandonTimer.StartTimer(
            abandonDuration,
            TriggerCornEffect
        );
    }

    void TriggerCornEffect()
    {
        explodeTrait.Explode();
        PushIngredientToPool(this.catchable);
    }
}
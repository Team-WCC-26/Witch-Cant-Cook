using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientCorn : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Time Settings")]
    [SerializeField] private float holdDuration = 3f;
    [SerializeField] private float abandonDuration = 10f;

    private IngredientTraitExplode explodeTrait;
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

        // 활성화된 시점에 아무도 들고 있지 않다면 방치 타이머부터 시작
        if (!catchable.IsHold)
        {
            StartAbandonTimer();
        }
    }

    private void OnDisable()
    {
        catchable.OnPicked -= OnPicked;
        catchable.OnDropped -= OnDropped;
        holdTimer.Stop();
        abandonTimer.Stop();

        PushIngredientToPool(catchable);

    }

    private void Update()
    {
        float dt = Time.deltaTime;
        holdTimer.Tick(dt);
        abandonTimer.Tick(dt);
    }

    private void OnPicked()
    {
        abandonTimer.Stop();
        StartHoldTimer();
    }

    private void OnDropped()
    {
        holdTimer.Stop();
        StartAbandonTimer();
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
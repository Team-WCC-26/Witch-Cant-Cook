using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientOnion : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Time Settings")]
    [Tooltip("들고 있는 상태로 이 시간이 지나면 처음 활성화되어 영역을 생성한다.")]
    [SerializeField] private float initialHoldDuration = 3f;
    [Tooltip("첫 활성화 이후, 영역을 반복 생성하는 주기(쿨타임).")]
    [SerializeField] private float spawnCooldown = 2f;

    private readonly IngredientTraitTimer holdTimer = new();
    private IngredientTraitAreaCreator areaCreator;

    [Header("Area")]
    [SerializeField] private Define.eIngredient eArea = Define.eIngredient.OnionLiquid;


    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
        areaCreator = GetComponent<IngredientTraitAreaCreator>();
    }

    private void OnEnable()
    {
        catchable.OnPicked += OnPicked;
        catchable.OnDropped += OnDropped;

    }

    private void OnDisable()
    {
        catchable.OnPicked -= OnPicked;
        catchable.OnDropped -= OnDropped;
        holdTimer.Stop();

    }

    private void Update()
    {
        float dt = Time.deltaTime;
        holdTimer.Tick(dt);
    }

    private void OnPicked()
    {
        StartHoldTimer();
    }

    private void OnDropped()
    {
        holdTimer.Stop();
    }

    private void StartHoldTimer()
    {
        // 1단계: 처음 initialHoldDuration(3초)만큼 들고 있어야 활성화된다.
        holdTimer.StartTimer(
            initialHoldDuration,
            ActivateSpawnLoop
        );
    }

    private void ActivateSpawnLoop()
    {
        // 3초를 채운 시점에 첫 영역을 생성하고,
        // 2단계: 이후로는 spawnCooldown(2초)마다 반복 생성한다.
        SpawnTearArea();
        holdTimer.StartLoop(spawnCooldown, SpawnTearArea);
    }

    private void SpawnTearArea()
    {
        if (areaCreator != null)
        {
            areaCreator.CreateArea(eArea);
        }
        else
        {
            Debug.LogWarning("IngredientTraitAreaCreator is not assigned.");
        }
    }
}
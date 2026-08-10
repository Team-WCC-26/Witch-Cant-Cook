using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(CatchableObj))]
public class IngredientOnion : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Time Settings")]
    [SerializeField] private float holdDuration = 3f;

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
        holdTimer.StartTimer(
            holdDuration,
            SpawnTearArea
        );
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

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

    [Header("Effect")]
    [SerializeField] private GameObject tearArea;


    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
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

        PushIngredientToPool(catchable);

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
        Debug.Log($"SpawnTearArea - Position: {transform.position}");
        Instantiate(tearArea, transform.position, Quaternion.identity);

        // 积己 夸没 菩哦 傈价
    }
}

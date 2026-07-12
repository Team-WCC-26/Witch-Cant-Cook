using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitBottle : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Break")]
    [SerializeField] private float breakImpactThreshold = 8f; // 충격량이 이 값 이상이면 깨짐

    [Header("Honey Area")]
    [SerializeField] private float honeyRadius = 3f;

    [Header("Effect")]
    [SerializeField] private ParticleSystem breakEffect;

    private bool isBroken;

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
    }

    private void OnEnable()
    {
        isBroken = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (isBroken)
            return;

        if (collision.relativeVelocity.magnitude < breakImpactThreshold)
            return;

        Break();
    }

    private void Break()
    {
        isBroken = true;

        Vector3 spawnPos = transform.position;

        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2f))
        {
            spawnPos = hit.point;
        }

        SpawnHoneyArea(spawnPos);

        // 깨진 후에는 오브젝트 풀로 반납
        PushIngredientToPool(catchable);
    }

    private void SpawnHoneyArea(Vector3 position)
    {
        Debug.Log($"SpawnHoneyArea - Position: {position}");
        // 생성 요청 패킷 전송
    }
}
using Protocol;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitBottle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Break")]
    [SerializeField] private float breakImpactThreshold = 8f; // 충격량이 이 값 이상이면 깨짐

    [Header("Honey Area")]
    [SerializeField] private float honeyRadius = 3f;

    [Header("Effect")]
    [SerializeField] private ParticleSystem breakEffect;
    [SerializeField] private GameObject honeyArea;

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
    }

    private void SpawnHoneyArea(Vector3 position)
    {
        IngredientSpawnPacket packet = new IngredientSpawnPacket
        {
            // Ingredient 테이블에 생성되는 영역들도 추가하던가 해야될 것 같음
        };
        // Todo : 생성 요청 패킷 전송

        Instantiate(honeyArea, position, Quaternion.identity);
    }
}
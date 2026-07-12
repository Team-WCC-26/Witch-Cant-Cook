using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitSquid : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Ink")]
    [SerializeField] private float holdDuration = 3f;
    [SerializeField] private float blindDuration = 3f;
    [SerializeField] private float cooldown = 10f;
    [SerializeField] private float impactThreshold = 6f;
    [SerializeField] private float radius = 10f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Effect")]
    [SerializeField] private ParticleSystem inkEffect;

    private float holdTimer;
    private float cooldownTimer;

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
    }

    private void Update()
    {
        UpdateCooldown();
        UpdateHold();
    }

    private void UpdateCooldown()
    {
        if (cooldownTimer > 0f)
            cooldownTimer -= Time.deltaTime;
    }

    private void UpdateHold()
    {
        if (!catchable.IsHold)
        {
            holdTimer = 0f;
            return;
        }

        holdTimer += Time.deltaTime;

        if (holdTimer < holdDuration)
            return;

        holdTimer = 0f;

        TrySpray(SprayHolder);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.relativeVelocity.magnitude < impactThreshold)
            return;

        TrySpray(SprayAround);
    }

    private void TrySpray(System.Action sprayAction)
    {
        if (cooldownTimer > 0f)
            return;

        cooldownTimer = cooldown;

        if (inkEffect != null)
            inkEffect.Play();

        sprayAction?.Invoke();
    }

    /// <summary>
    /// 들고 있는 플레이어에게 먹물
    /// </summary>
    private void SprayHolder()
    {
        PlayerBrain holder = catchable.Holder;

        if (holder == null)
            return;

        //holder.EffectController.ApplyBlind(blindDuration);
    }

    /// <summary>
    /// 주변 플레이어 모두에게 먹물
    /// </summary>
    private void SprayAround()
    {
        Collider[] hits =
            Physics.OverlapSphere(transform.position, radius, playerLayer);

        foreach (Collider hit in hits)
        {
            if (hit.TryGetComponent(out PlayerBrain player))
            {
                //player.EffectController.ApplyBlind(blindDuration);
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.black;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
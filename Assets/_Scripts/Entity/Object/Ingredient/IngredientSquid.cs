using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientSquid : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Time Settings")]
    [SerializeField] private float holdDuration = 3f;
    [SerializeField] private float blindDuration = 3f;
    [SerializeField] private float cooldown = 10f;

    [Header("Spray Settings")]
    [SerializeField] private float impactThreshold = 6f;
    [SerializeField] private float radius = 10f;
    [SerializeField] private LayerMask playerLayer;

    [Header("Effect")]
    //[SerializeField] private ParticleSystem inkEffect;

    private readonly IngredientTraitTimer holdTimer = new();
    private readonly IngredientTraitTimer cooldownTimer = new();

    private bool canSpray = true;

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
        Debug.Log($"[Squid] OnEnable called");

        canSpray = true;
        catchable.OnPicked += OnPicked;
        catchable.OnDropped += OnDropped;
        Debug.Log($"[Squid] OnEnable called");
    }

    private void OnDisable()
    {
        catchable.OnPicked -= OnPicked;
        catchable.OnDropped -= OnDropped;

        holdTimer.Stop();
        cooldownTimer.Stop();

        PushIngredientToPool(catchable);

    }

    private void Update()
    {
        holdTimer.Tick(Time.deltaTime);
        cooldownTimer.Tick(Time.deltaTime);
    }

    private void OnPicked()
    {
        Debug.Log($"[Squid] OnPicked 핸들러 호출됨 - {gameObject.name}");
        holdTimer.StartLoop(holdDuration, () => TrySpray(SprayHolder));
    }
    private void OnDropped()
    {
        holdTimer.Stop();
    }


    private void TrySpray(System.Action sprayAction)
    {
        Debug.Log($"TrySpray called, canSpray={canSpray}");
        if (!canSpray)
            return;

        canSpray = false;

        cooldownTimer.StartTimer(
            cooldown,
            () => canSpray = true);

        //inkEffect?.Play();

        sprayAction?.Invoke();
    }

    /// <summary>
    /// 들고 있는 플레이어에게 먹물
    /// </summary>
    private void SprayHolder()
    {
        PlayerBrain holder = catchable.Holder;
        Debug.Log($"SprayHolder called, holder={holder}");

        if (holder == null)
            return;

        holder.EffectController.ApplyBlind(blindDuration);
    }

    /// <summary>
    /// 주변 플레이어 모두에게 먹물 - 혹시 몰라서 구현
    /// </summary>
    private void SprayAround()
    {
        Collider[] hits =
            Physics.OverlapSphere(transform.position, radius, playerLayer);

        foreach (Collider hit in hits)
        {
            if (hit.TryGetComponent(out PlayerBrain player))
            {
                player.EffectController.ApplyBlind(blindDuration);
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
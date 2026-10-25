using UnityEngine;

[RequireComponent(typeof(CatchableObj), typeof(IngredientTraitExplode))]
public class IngredientCorn : IngredientTrait
{
    private const float ExplosionDelay = 3f;
    private const float WarningStartTime = 0.9f;
    private const float WarningDuration = 2.1f;

    [Header("References")]
    [SerializeField] private CatchableObj catchable;
    [Tooltip("경고 애니메이션을 적용할 시각 오브젝트. 물리 루트와 분리된 자식 오브젝트를 지정한다.")]
    [SerializeField] private Transform warningVisual;
    [Tooltip("추후 제작된 경고 클립을 연결한다. 비워 둬도 폭발은 정상 작동하며, 연결 시 충돌 0.9초 후부터 재생한다.")]
    [SerializeField] private AnimationClip warningAnimation;

    [Header("Impact")]
    [Tooltip("이 상대 충돌 속도(m/s) 이상이면 3초 후 폭발한다.")]
    [SerializeField, Min(0f)] private float cornImpactThreshold = 3f;

    private IngredientTraitExplode explodeTrait;
    private readonly IngredientTraitTimer explosionTimer = new();
    private Renderer[] warningRenderers;
    private MaterialPropertyBlock[][] originalPropertyBlocks;
    private MaterialPropertyBlock warningPropertyBlock;
    private Vector3 originalVisualPosition;
    private Vector3 originalVisualScale;
    private Quaternion originalVisualRotation;
    private float elapsed;
    private bool isArmed;
    private bool hasExploded;

    public float CornImpactThreshold
    {
        get => cornImpactThreshold;
        set => cornImpactThreshold = Mathf.Max(0f, value);
    }

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        warningPropertyBlock = new MaterialPropertyBlock();
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();

        explodeTrait = GetComponent<IngredientTraitExplode>();
        if (warningVisual == null)
        {
            MeshFilter visual = GetComponentInChildren<MeshFilter>();
            if (visual != null) warningVisual = visual.transform;
        }

        warningRenderers = GetComponentsInChildren<Renderer>();
        if (warningVisual != null)
        {
            originalVisualPosition = warningVisual.localPosition;
            originalVisualScale = warningVisual.localScale;
            originalVisualRotation = warningVisual.localRotation;
        }
    }

    protected override void StartTrait()
    {
        explosionTimer.Stop();
        elapsed = 0f;
        isArmed = false;
        hasExploded = false;
        CapturePropertyBlocks();
    }

    protected override void StopTrait()
    {
        explosionTimer.Stop();
        isArmed = false;
        RestoreWarningVisual();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsTraitAvailable || isArmed || hasExploded)
            return;

        if (IsSelfCollision(collision.collider))
            return;

        if (collision.relativeVelocity.magnitude < cornImpactThreshold)
            return;

        if (!HasExplosionAuthority())
            return;

        // 첫 유효 충돌에서만 예약한다. 잡거나 내려놓거나 다시 충돌해도 취소하지 않는다.
        isArmed = true;
        elapsed = 0f;
        explosionTimer.StartTimer(ExplosionDelay, TriggerCornEffect);
    }

    private bool IsSelfCollision(Collider other)
    {
        if (other == null || other.transform.IsChildOf(transform))
            return true;

        PlayerBrain holder = catchable.Holder;
        if (holder == null) return false;

        if (other.GetComponentInParent<PlayerBrain>() == holder)
            return true;

        CatchableObj otherObject = other.GetComponentInParent<CatchableObj>();
        return otherObject != null && otherObject.Holder == holder;
    }

    private bool HasExplosionAuthority()
    {
        // 기존 충돌 재료와 동일하게 마지막으로 잡은 클라이언트만 교체를 요청한다.
        if (catchable.NetworkId == 0) return true;
        if (!string.IsNullOrEmpty(catchable.LastHolderPlayerId))
            return catchable.IsLocalOwner;

        // 아직 아무도 잡지 않은 옥수수는 한 클라이언트만 처리한다.
        PlayerSpawnManager manager = PlayerSpawnManager.Instance;
        if (manager == null || string.IsNullOrEmpty(manager.MyID)) return false;

        string authorityId = manager.MyID;
        foreach (PlayerBrain player in manager.Players)
        {
            if (player != null && !string.IsNullOrEmpty(player.PlayerId) &&
                string.CompareOrdinal(player.PlayerId, authorityId) < 0)
                authorityId = player.PlayerId;
        }

        return authorityId == manager.MyID;
    }

    private void Update()
    {
        if (!isArmed || hasExploded) return;

        elapsed += Time.deltaTime;
        UpdateWarningVisual();
        explosionTimer.Tick(Time.deltaTime);
    }

    private void UpdateWarningVisual()
    {
        if (elapsed < WarningStartTime) return;

        float animationTime = Mathf.Clamp(elapsed - WarningStartTime, 0f, WarningDuration);
        float progress = animationTime / WarningDuration;

        if (warningVisual != null && warningAnimation != null)
            warningAnimation.SampleAnimation(warningVisual.gameObject, animationTime);

        for (int i = 0; i < warningRenderers.Length; i++)
        {
            Renderer renderer = warningRenderers[i];
            if (renderer == null) continue;

            Material[] materials = renderer.sharedMaterials;
            for (int j = 0; j < materials.Length; j++)
            {
                Material material = materials[j];
                if (material == null) continue;

                renderer.GetPropertyBlock(warningPropertyBlock, j);
                TintWarningColor(material, "_BaseColor", progress);
                TintWarningColor(material, "_Color", progress);
                renderer.SetPropertyBlock(warningPropertyBlock, j);
            }
        }
    }

    private void TintWarningColor(Material material, string property, float progress)
    {
        if (!material.HasProperty(property)) return;

        Color original = material.GetColor(property);
        Color red = new(1f, 0f, 0f, original.a);
        warningPropertyBlock.SetColor(property, Color.Lerp(original, red, progress));
    }

    private void CapturePropertyBlocks()
    {
        originalPropertyBlocks = new MaterialPropertyBlock[warningRenderers.Length][];
        for (int i = 0; i < warningRenderers.Length; i++)
        {
            Renderer renderer = warningRenderers[i];
            if (renderer == null) continue;

            int materialCount = renderer.sharedMaterials.Length;
            originalPropertyBlocks[i] = new MaterialPropertyBlock[materialCount];
            for (int j = 0; j < materialCount; j++)
            {
                MaterialPropertyBlock block = new();
                renderer.GetPropertyBlock(block, j);
                originalPropertyBlocks[i][j] = block;
            }
        }
    }

    private void RestoreWarningVisual()
    {
        if (warningVisual != null)
        {
            warningVisual.localPosition = originalVisualPosition;
            warningVisual.localScale = originalVisualScale;
            warningVisual.localRotation = originalVisualRotation;
        }

        if (originalPropertyBlocks == null) return;
        for (int i = 0; i < warningRenderers.Length; i++)
        {
            Renderer renderer = warningRenderers[i];
            MaterialPropertyBlock[] blocks = originalPropertyBlocks[i];
            if (renderer == null || blocks == null) continue;

            int materialCount = Mathf.Min(blocks.Length, renderer.sharedMaterials.Length);
            for (int j = 0; j < materialCount; j++)
                renderer.SetPropertyBlock(blocks[j].isEmpty ? null : blocks[j], j);
        }
    }

    private void TriggerCornEffect()
    {
        if (!IsTraitAvailable || hasExploded) return;
        hasExploded = true;
        explodeTrait.Explode();
        PushIngredientToPool(catchable);
    }
}

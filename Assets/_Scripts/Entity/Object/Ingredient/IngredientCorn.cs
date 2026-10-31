using Protocol;
using UnityEngine;

[RequireComponent(typeof(CatchableObj), typeof(IngredientTraitExplode))]
public class IngredientCorn : IngredientTrait
{
    private const float HoldDuration = 3f;
    private const float AbandonDuration = 10f;
    private const float WarningDuration = 2.1f;

    [Header("References")]
    [SerializeField] private CatchableObj catchable;
    [Tooltip("경고 애니메이션을 적용할 시각 오브젝트. 물리 루트와 분리된 자식 오브젝트를 지정한다.")]
    [SerializeField] private Transform warningVisual;
    [Tooltip("서버 폭발 예정 시점의 마지막 2.1초 동안 재생하는 경고 클립. 실제 폭발은 서버 패킷으로만 실행한다.")]
    [SerializeField] private AnimationClip warningAnimation;

    private IngredientTraitExplode explodeTrait;
    private Renderer[] warningRenderers;
    private MaterialPropertyBlock[][] originalPropertyBlocks;
    private MaterialPropertyBlock warningPropertyBlock;
    private Vector3 originalVisualPosition;
    private Vector3 originalVisualScale;
    private Quaternion originalVisualRotation;
    private float elapsed;
    private PlayerBrain warningHolder;
    private bool hasExploded;
    private bool warningSoundStarted;
    private SoundHandle warningSound;

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
        elapsed = 0f;
        warningHolder = catchable.Holder;
        hasExploded = false;
        warningSoundStarted = false;
        CapturePropertyBlocks();
    }

    protected override void StopTrait()
    {
        warningSound.Stop();
        warningSoundStarted = false;
        warningHolder = null;
        RestoreWarningVisual();
    }

    private bool HasReplacementAuthority()
    {
        // 폭발은 모든 클라이언트가 처리하고, 팝콘 생성만 한 클라이언트가 요청한다.
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
        if (!IsTraitAvailable || hasExploded) return;

        if (warningHolder != catchable.Holder)
        {
            warningSound.Stop();
            warningSoundStarted = false;
            warningHolder = catchable.Holder;
            elapsed = 0f;
            RestoreWarningVisual();
        }

        // 경고 연출만 예측한다. 이 시간이 지나도 서버 패킷 없이는 폭발하지 않는다.
        elapsed += Time.deltaTime;
        UpdateWarningVisual();
    }

    private void UpdateWarningVisual()
    {
        float warningStartTime = (warningHolder != null ? HoldDuration : AbandonDuration) - WarningDuration;
        if (elapsed < warningStartTime) return;
        if (!warningSoundStarted)
        {
            warningSoundStarted = true;
            warningSound = SoundManager.GetOrCreate().Play(SoundId.CornExplosionWarning, transform.position, transform);
        }

        float animationTime = Mathf.Clamp(elapsed - warningStartTime, 0f, WarningDuration);
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

    public void HandleServerExplosion(IngredientCornPacket packet)
    {
        if (packet.EntityId != catchable.NetworkId || !IsTraitAvailable || hasExploded) return;
        hasExploded = true;
        warningSound.Stop();

        Vector3 origin = packet.ExplosionOrigin.HasValue
            ? new Vector3(packet.ExplosionOrigin.Value.X, packet.ExplosionOrigin.Value.Y, packet.ExplosionOrigin.Value.Z)
            : transform.position;
        SoundManager.GetOrCreate().Play(SoundId.CornExplosion, origin);
        explodeTrait.Explode(origin, packet.ExplosionSeed, HasReplacementAuthority());
        // 옥수수 제거는 서버가 보낸 기존 엔티티 제거 통지를 따른다.
    }
}

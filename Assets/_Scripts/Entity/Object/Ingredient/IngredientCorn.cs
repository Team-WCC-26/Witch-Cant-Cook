using Protocol;
using Server;
using UnityEngine;

[RequireComponent(typeof(CatchableObj), typeof(IngredientTraitExplode))]
public class IngredientCorn : IngredientTrait
{
    private const float WarningDuration = 2.1f;

    [Header("Collision")]
    [Tooltip("폭발을 예약하는 최소 충격량(N·s). Collision.impulse.magnitude와 비교합니다.")]
    [SerializeField, Min(0f)] private float cornImpactThreshold = 1f;

    [Header("References")]
    [SerializeField] private CatchableObj catchable;
    [Tooltip("서버 폭발 예정 시점의 마지막 2.1초 동안 재생하는 경고 클립. 실제 폭발은 서버 패킷으로만 실행한다.")]
    [SerializeField] private AnimationClip warningAnimation;

    private IngredientTraitExplode explodeTrait;
    private Renderer[] warningRenderers;
    private MaterialPropertyBlock[][] originalPropertyBlocks;
    private MaterialPropertyBlock warningPropertyBlock;
    private IngredientReaction reaction;
    private double explosionTime;
    private bool explosionScheduled;
    private bool collisionReported;
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
        reaction = GetComponent<IngredientReaction>();
        warningRenderers = GetComponentsInChildren<Renderer>();
    }

    protected override void StartTrait()
    {
        explosionTime = 0d;
        explosionScheduled = false;
        collisionReported = false;
        hasExploded = false;
        warningSoundStarted = false;
        CapturePropertyBlocks();
    }

    protected override void StopTrait()
    {
        warningSound.Stop();
        warningSoundStarted = false;
        explosionScheduled = false;
        collisionReported = false;
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

    private void OnCollisionEnter(Collision collision)
    {
        float impact = collision.impulse.magnitude;
        bool thresholdMet = impact >= cornImpactThreshold;
        string ignoreReason = GetCollisionIgnoreReason(collision.collider);
        string action = ignoreReason != null ? $"IGNORE:{ignoreReason}" :
            thresholdMet ? "REQUEST_EXPLOSION" : "BELOW_THRESHOLD";
        Debug.Log($"[Corn Impact] entity={catchable?.NetworkId}, other={collision.collider?.name}, impulse={impact:F4} N·s, threshold={cornImpactThreshold:F4} N·s, thresholdMet={thresholdMet}, action={action}", this);
        if (ignoreReason != null || !thresholdMet) return;

        collisionReported = true;
        Debug.Log($"[Corn Collision TX] entity={catchable.NetworkId}, packet=C_IngredientCollision", this);
        _ = ServerManager.Instance.SendData(PacketSerializer.Serialize(new IngredientCollisionPacket
        {
            EntityId = catchable.NetworkId
        }));
    }

    private string GetCollisionIgnoreReason(Collider other)
    {
        if (!IsTraitAvailable) return "TRAIT_UNAVAILABLE";
        if (catchable == null) return "MISSING_CATCHABLE";
        if (hasExploded) return "ALREADY_EXPLODED";
        if (explosionScheduled) return "COUNTDOWN_RUNNING";
        if (collisionReported) return "COLLISION_ALREADY_SENT";
        if (catchable.NetworkId == 0) return "NO_NETWORK_ID";
        if (!HasReplacementAuthority()) return "NOT_AUTHORITY";
        if (ServerManager.Instance == null || !ServerManager.Instance.IsConnected) return "DISCONNECTED";
        if (other == null || other.isTrigger || other.transform.IsChildOf(transform)) return "SELF_OR_TRIGGER";

        // Exclude holding contacts while allowing the world and other players to arm the corn.
        PlayerBrain holder = catchable.Holder;
        if (holder != null &&
            (other.GetComponentInParent<PlayerBrain>() == holder ||
             other.transform.IsChildOf(holder.transform)))
            return "HOLDER_COLLIDER";
        CatchableObj otherCatchable = other.GetComponentInParent<CatchableObj>();
        if (holder != null && otherCatchable != null && otherCatchable.Holder == holder)
            return "HOLDER_EQUIPMENT";
        return null;
    }
    public void HandleServerExplosionStart(IngredientCornExplodeStartPacket packet)
    {
        if (packet.EntityId != catchable.NetworkId || !IsTraitAvailable ||
            hasExploded || explosionScheduled) return;

        // The server owns the fuse. Picking up, dropping, and further impacts never reset it.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Corn Start RX] entity={packet.EntityId}, delayMs={packet.ExplosionDelayMs}", this);
#endif
        explosionScheduled = true;
        collisionReported = true;
        explosionTime = Time.unscaledTimeAsDouble + System.Math.Max(0L, packet.ExplosionDelayMs) / 1000d;
        UpdateWarningVisual();
    }

    private void Update()
    {
        if (!IsTraitAvailable || hasExploded || !explosionScheduled) return;
        UpdateWarningVisual();
    }

    private void UpdateWarningVisual()
    {
        double remaining = explosionTime - Time.unscaledTimeAsDouble;
        if (remaining > WarningDuration) return;
        if (!warningSoundStarted)
        {
            warningSoundStarted = true;
            Debug.Log($"[Corn Warning Start] entity={catchable.NetworkId}, remainingSeconds={remaining:F3}, durationSeconds={WarningDuration:F1}, clipAssigned={warningAnimation != null}, reactionPresent={reaction != null}", this);
            warningSound = SoundManager.GetOrCreate().Play(SoundId.CornExplosionWarning, transform.position, transform);
        }

        // For a 3-second fuse, wait 0.9 seconds and play the entire clip over the final 2.1 seconds.
        // Reaching the deadline only holds the last pose; the explosion still requires its packet.
        float progress = Mathf.Clamp01((float)((WarningDuration - remaining) / WarningDuration));

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
        if (reaction != null && warningAnimation != null)
            reaction.SampleWarningAnimation(warningAnimation, progress * warningAnimation.length);
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
        reaction?.ClearWarningAnimation();

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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Corn Explosion RX] entity={packet.EntityId}", this);
#endif
        hasExploded = true;
        explosionScheduled = false;
        warningSound.Stop();
        RestoreWarningVisual();

        Vector3 origin = packet.ExplosionOrigin.HasValue
            ? new Vector3(packet.ExplosionOrigin.Value.X, packet.ExplosionOrigin.Value.Y, packet.ExplosionOrigin.Value.Z)
            : transform.position;
        SoundManager.GetOrCreate().Play(SoundId.CornExplosion, origin);
        explodeTrait.Explode(origin, packet.ExplosionSeed, HasReplacementAuthority());
        // 수신 라우터가 동일 엔티티 ID의 팝콘 프리팹으로 교체한다.
    }
}

using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerBrain))]
public sealed class PlayerRocketController : MonoBehaviour
{
    private const float CollisionSkin = 0.02f;
    private const float KnockbackDuration = 0.35f;

    private struct BlockingHit
    {
        public Collider collider;
        public float distance;
    }

    [Tooltip("추후 Animator에 추가할 로켓 자세 bool 파라미터. 없어도 비행은 작동한다.")]
    [SerializeField] private string flightAnimatorParameter = "RocketFlying";

    private PlayerBrain brain;
    private IngredientCarrot carrot;
    private Vector3 launchDirection;
    private Vector3 flightDirection;
    private Quaternion heldRotation;
    private float elapsed;
    private float startedAt;
    private bool remoteFlight;
    private bool savedGravity;
    private float savedDamping;
    private CollisionDetectionMode savedCollisionMode;
    private Vector3 previousRemotePosition;
    private bool hasRemotePosition;

    private readonly List<(Collider player, Collider ingredient)> ignoredCollisions = new();
    private readonly HashSet<int> hitPlayers = new();
    private readonly HashSet<Collider> separatingContacts = new();

    public bool IsFlying { get; private set; }
    public Vector3 FlightDirection => flightDirection;
    public IngredientCarrot Carrot => carrot;

    public void Initialize(PlayerBrain player) => brain = player;

    private void Awake() => brain = GetComponent<PlayerBrain>();

    private void OnEnable()
    {
        if (brain == null) brain = GetComponent<PlayerBrain>();
    }

    public bool TryStartFlight(IngredientCarrot ingredient)
    {
        if (IsFlying) return false;
        if (brain == null) brain = GetComponent<PlayerBrain>();
        if (brain == null) return RejectLaunch("플레이어 참조가 없습니다.");
        if (ingredient == null || !ingredient.CanRocket) return RejectLaunch("손질되지 않은 활성 당근이 필요합니다.");
        if (!isActiveAndEnabled) return RejectLaunch("로켓 컨트롤러가 비활성화되어 있습니다.");
        if (brain.StateResolver is RemotePlayerStateResolver) return RejectLaunch("원격 플레이어는 직접 발사할 수 없습니다.");
        if (brain.Rb == null || brain.Col == null) return RejectLaunch("플레이어 Rigidbody 또는 Collider가 없습니다.");
        if (brain.Rb.isKinematic) return RejectLaunch("플레이어 Rigidbody가 Kinematic 상태입니다.");
        if (brain.Interact.HeldObj != ingredient.Catchable || ingredient.Catchable.Holder != brain)
            return RejectLaunch("당근의 잡기 상태와 플레이어 참조가 일치하지 않습니다.");
        if (brain.Health != null && brain.Health.IsRagdoll) return RejectLaunch("기절 상태에서는 발사할 수 없습니다.");
        if (brain.StateResolver != null && brain.StateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default)
            return RejectLaunch("현재 플레이어 상태에서는 발사할 수 없습니다.");
        if (brain.Input != null && brain.Input.IsInputBlocked) return RejectLaunch("플레이어 입력이 차단되어 있습니다.");
        if (brain.ActionController.Movement.IsForcedMovementActive) return RejectLaunch("다른 강제 이동이 진행 중입니다.");

        brain.ActionController.CancelAction();
        carrot = ingredient;
        elapsed = 0f;
        startedAt = Time.time;
        remoteFlight = false;
        launchDirection = GetAimDirection();
        flightDirection = launchDirection;
        heldRotation = carrot.transform.localRotation;
        hitPlayers.Clear();

        savedGravity = brain.Rb.useGravity;
        savedDamping = brain.Rb.linearDamping;
        savedCollisionMode = brain.Rb.collisionDetectionMode;
        brain.Rb.useGravity = false;
        brain.Rb.linearDamping = 0f;
        brain.Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        brain.Rb.angularVelocity = Vector3.zero;
        IsFlying = true;
        carrot.SetPilot(this);
        carrot.SetFlightPresentation(true);
        SetFlightAnimation(true);
        IgnoreNearbyIngredients();
        LiftForLaunch();
        carrot.transform.rotation = Quaternion.LookRotation(flightDirection, Vector3.up) *
                                    carrot.FlightRotationOffset;
        Physics.SyncTransforms();
        brain.Rb.linearVelocity = flightDirection * Mathf.Max(0.1f, carrot.Settings.rocketSpeed);
        (brain.StateResolver as LocalPlayerStateResolver)?.SendRocketSnapshot(false);
        return true;
    }

    private bool RejectLaunch(string reason)
    {
        Debug.LogWarning($"[CarrotRocket] 발사 실패: {reason}", this);
        return false;
    }

    public void UpdateFlightState(PlayerCombinedState state)
    {
        if (!IsFlying) return;
        if (carrot == null || !carrot.CanRocket || brain.Interact.HeldObj != carrot.Catchable ||
            state.PhysicalMode != PlayerPhysicalMode.Default ||
            (brain.Health != null && brain.Health.IsRagdoll) ||
            (brain.Input != null && brain.Input.IsInputBlocked) ||
            (!remoteFlight && Time.time - startedAt >= Mathf.Clamp(carrot.Settings.rocketDuration, 0.05f, 3f)))
            StopFlight();
    }

    // Called before normal movement so only one controller writes the player's velocity.
    public bool FixedTick(PlayerCombinedState state)
    {
        if (!IsFlying) return false;
        UpdateFlightState(state);
        if (!IsFlying) return false;
        if (remoteFlight) return true; // Remote motion comes from existing movement snapshots.

        float duration = Mathf.Clamp(carrot.Settings.rocketDuration, 0.05f, 3f);
        elapsed = Mathf.Max(elapsed, Time.time - startedAt);
        if (elapsed >= duration)
        {
            StopFlight();
            return false;
        }

        float stepTime = Mathf.Min(Time.fixedDeltaTime, duration - elapsed);
        CarrotRocketSettings settings = carrot.Settings;
        Vector3 desired = Vector3.RotateTowards(launchDirection, GetAimDirection(),
            Mathf.Clamp(settings.rocketTurnMaxAngle, 0f, 180f) * Mathf.Deg2Rad, 0f).normalized;
        flightDirection = Vector3.RotateTowards(flightDirection, desired,
            Mathf.Max(0f, settings.rocketTurnSpeed) * Mathf.Deg2Rad * stepTime, 0f).normalized;

        IgnoreNearbyIngredients();
        float distance = Mathf.Max(0.1f, settings.rocketSpeed) * stepTime;
        if (TryFindBlockingHit(distance, out BlockingHit hit))
        {
            brain.Rb.position += flightDirection * Mathf.Max(0f, hit.distance - CollisionSkin);
            HandleImpact(hit.collider);
            return true;
        }

        brain.Rb.linearVelocity = flightDirection * (distance / Time.fixedDeltaTime);
        brain.ActionController.Movement.UpdateForcedMovementSpeed();
        elapsed += stepTime;
        return true;
    }

    public void StopFlight()
    {
        if (!IsFlying) return;
        IsFlying = false;
        bool wasRemote = remoteFlight;
        remoteFlight = false;

        RestoreIgnoredCollisions();
        if (!wasRemote && brain != null && brain.Rb != null)
        {
            brain.Rb.useGravity = savedGravity;
            brain.Rb.linearDamping = savedDamping;
            brain.Rb.collisionDetectionMode = brain.Rb.isKinematic
                ? CollisionDetectionMode.ContinuousSpeculative : savedCollisionMode;
            if (!brain.Rb.isKinematic)
            {
                brain.Rb.linearVelocity = Vector3.zero;
                brain.Rb.angularVelocity = Vector3.zero;
            }
            brain.ActionController.Movement.UpdateForcedMovementSpeed();
        }

        if (carrot != null)
        {
            carrot.transform.localRotation = heldRotation;
            carrot.SetPilot(null);
            carrot.SetFlightPresentation(false);
        }
        carrot = null;
        SetFlightAnimation(false);
        if (!wasRemote)
            (brain.StateResolver as LocalPlayerStateResolver)?.SendRocketSnapshot(true);
    }

    public void ApplyRemoteFlight(bool flying, Vector3 direction, Vector3 snapshotPosition)
    {
        if (brain == null || brain.StateResolver is not RemotePlayerStateResolver) return;

        if (direction.sqrMagnitude > 0.001f) flightDirection = direction.normalized;
        // Include the final non-flight snapshot to detect contact at the end of a flight.
        if (IsFlying && remoteFlight && hasRemotePosition)
            CheckRemotePlayerHits(previousRemotePosition, snapshotPosition);

        if (!flying)
        {
            StopFlight();
            hasRemotePosition = false;
            return;
        }

        IngredientCarrot heldCarrot = brain.Interact.HeldObj != null
            ? brain.Interact.HeldObj.GetComponent<IngredientCarrot>() : null;
        if (heldCarrot == null || !heldCarrot.CanRocket)
        {
            StopFlight();
            return;
        }

        if (!IsFlying)
        {
            carrot = heldCarrot;
            heldRotation = carrot.transform.localRotation;
            remoteFlight = true;
            IsFlying = true;
            hitPlayers.Clear();
            carrot.SetPilot(this);
            carrot.SetFlightPresentation(true);
            SetFlightAnimation(true);
        }
        flightDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : brain.transform.forward;
        previousRemotePosition = snapshotPosition;
        hasRemotePosition = true;
        CheckRemotePlayerHits(snapshotPosition, snapshotPosition);
    }

    private Vector3 GetAimDirection()
    {
        // The rendered player camera is the aiming reference, including vertical look direction.
        if (brain.PlayerCam != null) return brain.PlayerCam.transform.forward.normalized;
        if (brain.CameraController != null && brain.CameraController.isActiveAndEnabled)
            return brain.CameraController.AimDirection;
        return brain.transform.forward.normalized;
    }

    private void LiftForLaunch()
    {
        float height = Mathf.Max(0f, carrot.Settings.rocketLaunchHeight);
        if (height <= 0f) return;

        Physics.SyncTransforms();
        GetBodyCapsule(brain.transform.position, CollisionSkin,
            out Vector3 top, out Vector3 bottom, out float radius);
        foreach (RaycastHit hit in Physics.CapsuleCastAll(top, bottom, radius, Vector3.up,
            height + CollisionSkin, carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore))
        {
            if (IsIgnoredCollider(hit.collider) || Vector3.Dot(Vector3.up, hit.normal) >= -0.001f) continue;
            height = Mathf.Min(height, Mathf.Max(0f, hit.distance - CollisionSkin));
        }

        // Move the root immediately so the player, held carrot and first movement snapshot
        // all start above the floor. Ceiling clearance limits the lift distance.
        Vector3 liftedPosition = brain.Rb.position + Vector3.up * height;
        brain.Rb.position = liftedPosition;
        brain.transform.position = liftedPosition;
        Physics.SyncTransforms();
    }

    private void LateUpdate()
    {
        if (!IsFlying || carrot == null || flightDirection.sqrMagnitude < 0.001f) return;
        carrot.transform.rotation = Quaternion.LookRotation(flightDirection, Vector3.up) *
                                    carrot.FlightRotationOffset;
    }

    private void OnDisable() => StopFlight();

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsFlying || remoteFlight || IsIgnoredCollider(collision.collider)) return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            // Existing floor contact must not cancel a launch that moves along/away from it.
            if (Vector3.Dot(flightDirection, collision.GetContact(i).normal) >= -0.001f) continue;
            HandleImpact(collision.collider);
            return;
        }
    }

    private bool TryFindBlockingHit(float distance, out BlockingHit nearest)
    {
        nearest = default;
        float nearestDistance = float.PositiveInfinity;
        separatingContacts.Clear();
        GetBodyCapsule(brain.transform.position, CollisionSkin, out Vector3 top, out Vector3 bottom, out float radius);
        SelectBlockingOverlap(Physics.OverlapCapsule(top, bottom, radius,
            carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore), brain.Col, ref nearest, ref nearestDistance);

        // Held ingredient physics is disabled, so query its shape without enabling its collider.
        if (carrot.Catchable.Col is BoxCollider box)
        {
            Vector3 scale = box.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 halfSize = Vector3.Scale(box.size, scale) * 0.49f;
            SelectBlockingOverlap(Physics.OverlapBox(box.transform.TransformPoint(box.center), halfSize,
                box.transform.rotation, carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore),
                box, ref nearest, ref nearestDistance);
            SelectBlockingHit(Physics.BoxCastAll(box.transform.TransformPoint(box.center), halfSize,
                flightDirection, box.transform.rotation, distance + CollisionSkin,
                carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore), ref nearest, ref nearestDistance);
        }
        else
        {
            Renderer visual = carrot.GetComponentInChildren<MeshRenderer>();
            Vector3 center = visual != null ? visual.bounds.center : carrot.transform.position;
            float ingredientRadius = visual != null ? visual.bounds.extents.magnitude : 0.1f;
            SelectBlockingHit(Physics.SphereCastAll(center, Mathf.Max(0.01f, ingredientRadius), flightDirection,
                distance + CollisionSkin, carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore),
                ref nearest, ref nearestDistance);
        }
        SelectBlockingHit(Physics.CapsuleCastAll(top, bottom, radius, flightDirection,
            distance + CollisionSkin, carrot.Settings.collisionMask, QueryTriggerInteraction.Ignore),
            ref nearest, ref nearestDistance);
        return nearestDistance < float.PositiveInfinity;
    }

    private void SelectBlockingOverlap(Collider[] hits, Collider movingShape, ref BlockingHit nearest, ref float nearestDistance)
    {
        foreach (Collider hit in hits)
        {
            if (IsIgnoredCollider(hit)) continue;
            // An overlap query is not an impact. Floor contact and surfaces being left behind
            // must not cancel the first flight tick or produce a zero-distance sweep hit.
            if (!Physics.ComputePenetration(movingShape, movingShape.transform.position, movingShape.transform.rotation,
                hit, hit.transform.position, hit.transform.rotation, out Vector3 separation, out float depth) ||
                depth <= 0.0001f || Vector3.Dot(flightDirection, separation) >= -0.001f)
            {
                separatingContacts.Add(hit);
                continue;
            }
            nearest = new BlockingHit { collider = hit, distance = 0f };
            nearestDistance = 0f;
            separatingContacts.Remove(hit);
        }
    }

    private void SelectBlockingHit(RaycastHit[] hits, ref BlockingHit nearest, ref float nearestDistance)
    {
        foreach (RaycastHit hit in hits)
        {
            if (IsIgnoredCollider(hit.collider) || separatingContacts.Contains(hit.collider) ||
                Vector3.Dot(flightDirection, hit.normal) >= -0.001f) continue;
            if (hit.distance >= nearestDistance) continue;
            nearest = new BlockingHit { collider = hit.collider, distance = hit.distance };
            nearestDistance = hit.distance;
        }
    }

    private bool IsIgnoredCollider(Collider other)
    {
        if (other == null || other.transform.IsChildOf(brain.transform)) return true;
        if (other.GetComponentInParent<PlayerBrain>() == brain) return true;
        CatchableObj obj = other.GetComponentInParent<CatchableObj>();
        if (obj != null && (obj == carrot.Catchable || obj.Holder == brain)) return true;
        return IsOrdinaryIngredient(other);
    }

    private static bool IsOrdinaryIngredient(Collider other)
    {
        CatchableObj obj = other.GetComponentInParent<CatchableObj>();
        return obj != null && obj.Category == EntityCategory.Ingredient &&
               obj.GetComponent<RocketBlockingIngredient>() == null;
    }

    private void IgnoreNearbyIngredients()
    {
        float range = Mathf.Max(0.1f, carrot.Settings.rocketSpeed) * Time.fixedDeltaTime +
                      brain.Col.bounds.extents.magnitude + 2f;
        Collider[] nearby = Physics.OverlapSphere(brain.Col.bounds.center, range,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);
        Collider[] playerColliders = brain.GetComponentsInChildren<Collider>();
        foreach (Collider ingredientCollider in nearby)
        {
            if (!IsOrdinaryIngredient(ingredientCollider)) continue;
            foreach (Collider playerCollider in playerColliders)
            {
                if (!playerCollider.enabled || playerCollider.isTrigger || playerCollider == ingredientCollider) continue;
                if (playerCollider.GetComponentInParent<CatchableObj>() != null) continue;
                if (Physics.GetIgnoreCollision(playerCollider, ingredientCollider)) continue;
                Physics.IgnoreCollision(playerCollider, ingredientCollider, true);
                ignoredCollisions.Add((playerCollider, ingredientCollider));
            }
        }
    }

    private void RestoreIgnoredCollisions()
    {
        foreach (var pair in ignoredCollisions)
            if (pair.player != null && pair.ingredient != null)
                Physics.IgnoreCollision(pair.player, pair.ingredient, false);
        ignoredCollisions.Clear();
    }

    private void HandleImpact(Collider collider)
    {
        if (!IsFlying || carrot == null) return;
        if (elapsed <= Time.fixedDeltaTime)
            Debug.LogWarning($"[CarrotRocket] 발사 직후 '{collider.name}' 방향의 충돌로 비행이 종료되었습니다.", this);
        PlayerBrain victim = collider.GetComponentInParent<PlayerBrain>();
        if (victim != null) HitPlayer(victim);
        else
        {
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic)
                body.AddForce(flightDirection * Mathf.Max(0f, carrot.Settings.rocketImpactForce), ForceMode.Impulse);
        }
        StopFlight();
    }

    private void GetBodyCapsule(Vector3 position, float shrink, out Vector3 top, out Vector3 bottom, out float radius)
    {
        Bounds bounds = brain.Col.bounds;
        radius = Mathf.Max(0.01f, Mathf.Min(bounds.extents.x, bounds.extents.z) - shrink);
        float halfSegment = Mathf.Max(0f, bounds.extents.y - shrink - radius);
        Vector3 center = bounds.center + position - brain.transform.position;
        top = center + Vector3.up * halfSegment;
        bottom = center - Vector3.up * halfSegment;
    }

    private void CheckRemotePlayerHits(Vector3 from, Vector3 to)
    {
        if (carrot == null || brain.Col == null) return;
        GetBodyCapsule(from, -CollisionSkin, out Vector3 top, out Vector3 bottom, out float radius);
        Vector3 displacement = to - from;
        if (displacement.sqrMagnitude > 0.000001f)
        {
            foreach (RaycastHit hit in Physics.CapsuleCastAll(top, bottom, radius, displacement.normalized,
                displacement.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                PlayerBrain victim = hit.collider.GetComponentInParent<PlayerBrain>();
                if (victim != null) HitPlayer(victim);
            }
        }
        GetBodyCapsule(to, -CollisionSkin, out top, out bottom, out radius);
        foreach (Collider hit in Physics.OverlapCapsule(top, bottom, radius,
            Physics.AllLayers, QueryTriggerInteraction.Ignore))
        {
            PlayerBrain victim = hit.GetComponentInParent<PlayerBrain>();
            if (victim != null) HitPlayer(victim);
        }

        // The carrot tip can hit a player before the pilot's capsule reaches them.
        if (carrot.Catchable.Col is BoxCollider box)
        {
            Quaternion rotation = Quaternion.LookRotation(flightDirection, Vector3.up) *
                                  carrot.FlightRotationOffset;
            Vector3 scale = box.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 halfSize = Vector3.Scale(box.size, scale) * 0.5f + Vector3.one * CollisionSkin;
            Vector3 offset = carrot.transform.position - brain.transform.position +
                             rotation * Vector3.Scale(box.center, scale);
            if (displacement.sqrMagnitude > 0.000001f)
            {
                foreach (RaycastHit hit in Physics.BoxCastAll(from + offset, halfSize, displacement.normalized,
                    rotation, displacement.magnitude, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    PlayerBrain victim = hit.collider.GetComponentInParent<PlayerBrain>();
                    if (victim != null) HitPlayer(victim);
                }
            }
            foreach (Collider hit in Physics.OverlapBox(to + offset, halfSize, rotation,
                Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                PlayerBrain victim = hit.GetComponentInParent<PlayerBrain>();
                if (victim != null) HitPlayer(victim);
            }
        }
    }

    private void HitPlayer(PlayerBrain victim)
    {
        if (victim == brain || victim.StateResolver is RemotePlayerStateResolver || carrot == null) return;
        if (!hitPlayers.Add(victim.GetInstanceID())) return;
        victim.Rocket?.ReceiveImpact(brain.transform.position, flightDirection,
            Mathf.Max(0f, carrot.Settings.rocketImpactForce), carrot.GetImpactDamage());
    }

    private void ReceiveImpact(Vector3 attackerPosition, Vector3 direction, float force, float damage)
    {
        StopFlight();
        brain.GetComponent<PlayerHitWobble>()?.PlayHit(attackerPosition);
        brain.Health?.TakeDamage(damage);
        if (brain.Rb == null || brain.Rb.isKinematic) return;
        brain.ActionController.Movement.BeginForcedMovement(KnockbackDuration);
        brain.Rb.AddForce(direction.normalized * force, ForceMode.Impulse);
    }

    private void SetFlightAnimation(bool flying)
    {
        if (brain == null || brain.Animator == null || brain.Animator.runtimeAnimatorController == null ||
            string.IsNullOrEmpty(flightAnimatorParameter)) return;
        foreach (AnimatorControllerParameter parameter in brain.Animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Bool || parameter.name != flightAnimatorParameter) continue;
            brain.Animator.SetBool(parameter.nameHash, flying);
            return;
        }
    }
}

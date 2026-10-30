using System;
using System.Collections.Generic;
using UnityEngine;

// Both clients integrate the same throw packet with a fixed step. No new network messages.
// Contacts are queried explicitly so PhysX restitution, solver order and mesh rotation do not steer the ball.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Rigidbody), typeof(CatchableObj))]
public class IngredientTraitBounce : MonoBehaviour, IPoolable
{
    private const float Step = 0.01f;
    private const float Precision = 10000f;
    private const int MaxImpactsPerStep = 6;

    [SerializeField] private Rigidbody rb;
    [Header("Bounce")]
    [SerializeField, Range(0f, 1f)] private float restitution = 0.9f;
    [SerializeField, Range(0f, 1f)] private float tangentialRetention = 0.98f;
    [SerializeField, Min(0f)] private float minimumReboundSpeed = 0.6f;
    [Tooltip("월드 단위 충돌 반지름. 0이면 원본 메시에서 자동 계산합니다. 회전과 무관한 구형 판정을 사용합니다.")]
    [SerializeField, Min(0f)] private float collisionRadius = 0f;
    [SerializeField, Min(0.001f)] private float contactSkin = 0.005f;
    [Header("Push")]
    [SerializeField, Min(0f)] private float pushMultiplier = 0.15f;
    [SerializeField, Min(0f)] private float maxPushForce = 2f;
    [SerializeField, Min(0f)] private float minBounceSpeed = 2f;
    [SerializeField, Min(0f)] private float pushCooldown = 0.12f;
    [SerializeField] private LayerMask collisionMask = ~0;

    private CatchableObj source;
    private IngredientReaction reaction;
    private Collider[] ownColliders;
    private Collider shape;
    private Vector3 position;
    private Vector3 velocity;
    private Vector3 centerOffset;
    private float radius;
    private double accumulator;
    private double simulationTime;
    private bool running;
    private bool originalGravity;
    private bool waitingForEjectionCollision;
    private PotInteraction ejectionPot;
    private readonly RaycastHit[] hitBuffer = new RaycastHit[64];
    private readonly Dictionary<Rigidbody, double> lastPushTimes = new();
    private readonly List<(Collider self, Collider other)> ignoredPairs = new();
    private static Collider[] worldColliders;
    private static float cachedFixedTime = float.NegativeInfinity;

    public bool IsSimulating => running;
    public Vector3 Velocity => running ? velocity : rb != null ? rb.linearVelocity : Vector3.zero;
    private bool Available => source != null && !source.IsHold && source.ParentEntityId <= 0 &&
        !source.IsSubmissionPending && !source.IsSubmissionReturning &&
        (reaction == null || !reaction.IsActionCompleted(IngredientAction.Cut));

    #region Unity Callbacks

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        worldColliders = null;
        cachedFixedTime = float.NegativeInfinity;
    }

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        source = GetComponent<CatchableObj>();
        reaction = GetComponent<IngredientReaction>();
        ownColliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in ownColliders)
            if (collider.attachedRigidbody == rb && !collider.isTrigger) { shape = collider; break; }
        RemoveBounceMaterial();
    }

    private void OnEnable() => RemoveBounceMaterial();
    private void OnDisable() => StopSimulation();

    private void FixedUpdate()
    {
        if (!Available) { if (running) StopSimulation(false); return; }
        if (waitingForEjectionCollision) return;
        if (StageReturnFlow.IsInputBlocked) return;
        if (source.NetworkId > 0 && ConveyorBeltRegistry.IsOwnedByAnyBelt(source.NetworkId)) return;
        if (!running)
        {
            if (rb.isKinematic || shape == null || !shape.enabled) return;
            BeginMotion(rb.position, rb.linearVelocity);
        }
        SuppressNativePushes();
        accumulator += Time.fixedDeltaTime;
        while (accumulator + 0.0000001d >= Step)
        {
            SimulateStep();
            accumulator -= Step;
        }
        rb.MovePosition(position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!waitingForEjectionCollision || !isActiveAndEnabled || !Available) return;
        if (collision.collider.GetComponentInParent<PotInteraction>() == ejectionPot) return;

        // Resume next fixed step with the velocity resolved by native physics.
        waitingForEjectionCollision = false;
        ejectionPot = null;
    }

    #endregion

    public void ResetForPool() => StopSimulation(false);

    public void WaitForEjectionCollision(PotInteraction pot)
    {
        StopSimulation();
        if (!isActiveAndEnabled || !Available || rb == null || rb.isKinematic) return;

        ejectionPot = pot;
        waitingForEjectionCollision = true;
    }

    public void BeginMotion(Vector3 launchPosition, Vector3 launchVelocity)
    {
        bool gravityBefore = running ? originalGravity : rb.useGravity;
        StopSimulation(false);
        if (!isActiveAndEnabled || !Available || shape == null) return;
        originalGravity = gravityBefore;
        position = Quantize(launchPosition);
        velocity = Quantize(launchVelocity);
        accumulator = simulationTime = 0d;
        CalculateShape();
        RemoveBounceMaterial();
        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.position = position;
        running = true;
        SuppressNativePushes();
    }

    private void RemoveBounceMaterial()
    {
        if (ownColliders == null) return;
        foreach (Collider collider in ownColliders)
            if (collider != null && collider.attachedRigidbody == rb) collider.sharedMaterial = null;
    }

    private void CalculateShape()
    {
        Vector3 extents;
        Vector3 center;
        if (shape is MeshCollider mesh && mesh.sharedMesh != null)
        {
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            extents = Vector3.Scale(mesh.sharedMesh.bounds.extents, scale);
            center = Vector3.Scale(mesh.sharedMesh.bounds.center, scale);
        }
        else
        {
            extents = shape.bounds.extents;
            center = shape.bounds.center - transform.position;
        }
        // Ignore spin and horizontal mesh asymmetry; the same launch has the same query shape.
        radius = collisionRadius > 0f ? collisionRadius : Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z));
        radius = Mathf.Max(0.02f, Mathf.Round(radius * Precision) / Precision);
        centerOffset = new Vector3(0f, Mathf.Round(center.y * Precision) / Precision, 0f);
    }

    public void StopSimulation(bool restorePhysics = true)
    {
        waitingForEjectionCollision = false;
        ejectionPot = null;
        foreach (var pair in ignoredPairs)
            if (pair.self != null && pair.other != null) Physics.IgnoreCollision(pair.self, pair.other, false);
        ignoredPairs.Clear();
        lastPushTimes.Clear();
        if (running && restorePhysics && rb != null && source != null &&
                 !source.IsHold && source.ParentEntityId <= 0 &&
                 !source.IsSubmissionPending && !source.IsSubmissionReturning)
        {
            rb.isKinematic = false;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.useGravity = originalGravity;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Vector3.zero;
        }
        running = false;
        accumulator = simulationTime = 0d;
    }

    private void SuppressNativePushes()
    {
        if (worldColliders == null || cachedFixedTime != Time.fixedTime)
        {
            Physics.SyncTransforms();
            worldColliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
            cachedFixedTime = Time.fixedTime;
        }
        // Pair filtering leaves trigger zones and pickup raycasts intact.
        foreach (Collider other in worldColliders)
        {
            if (other == null || !other.enabled || other.isTrigger || other.attachedRigidbody == null ||
                other.attachedRigidbody == rb) continue;
            foreach (Collider self in ownColliders)
            {
                if (self == null || !self.enabled || self.isTrigger || self.attachedRigidbody != rb ||
                    Physics.GetIgnoreCollision(self, other)) continue;
                Physics.IgnoreCollision(self, other, true);
                ignoredPairs.Add((self, other));
            }
        }
    }

    private void SimulateStep()
    {
        simulationTime += Step;
        velocity += Physics.gravity * Step;
        float remaining = Step;
        for (int impact = 0; impact < MaxImpactsPerStep && remaining > 0.000001f; impact++)
        {
            Vector3 travel = velocity * remaining;
            float length = travel.magnitude;
            if (length < 0.000001f) break;
            if (!FindImpact(travel / length, length + contactSkin, out RaycastHit hit))
            {
                position += travel;
                break;
            }
            Vector3 normal = Quantize(hit.normal).normalized;
            float distance = Mathf.Max(0f, hit.distance - contactSkin);
            position += travel / length * Mathf.Min(distance, length);
            float incomingSpeed = velocity.magnitude;
            velocity = ReflectVelocity(velocity, normal, restitution, tangentialRetention, minimumReboundSpeed);
            PushAndDamage(hit.collider, normal, incomingSpeed);
            remaining *= 1f - Mathf.Clamp01(distance / length);
            position += normal * contactSkin;
        }
        position = Quantize(position);
        velocity = Quantize(velocity);
    }

    private bool FindImpact(Vector3 direction, float distance, out RaycastHit nearest)
    {
        int count = Physics.SphereCastNonAlloc(position + centerOffset, radius, direction, hitBuffer,
            distance, collisionMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = hitBuffer;
        if (count == hitBuffer.Length)
        {
            // Never make a decision from a truncated, unordered set of hits.
            hits = Physics.SphereCastAll(position + centerOffset, radius, direction,
                distance, collisionMask, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        nearest = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.attachedRigidbody == rb ||
                hit.collider.transform.IsChildOf(transform) ||
                Vector3.Dot(direction, hit.normal) >= -0.0001f) continue;
            if (!found || hit.distance < nearest.distance - 0.00001f ||
                (Mathf.Abs(hit.distance - nearest.distance) <= 0.00001f &&
                 string.CompareOrdinal(StableKey(hit.collider), StableKey(nearest.collider)) < 0))
            {
                nearest = hit;
                found = true;
            }
        }
        return found;
    }

    private static string StableKey(Collider collider)
    {
        PlayerBrain player = collider.GetComponentInParent<PlayerBrain>();
        if (player != null) return "P:" + player.PlayerId;
        CatchableObj item = collider.GetComponentInParent<CatchableObj>();
        if (item != null && item.NetworkId > 0) return "E:" + item.NetworkId.ToString("D20");
        string path = string.Empty;
        for (Transform node = collider.transform; node != null; node = node.parent)
            path = "/" + node.name + ":" + node.GetSiblingIndex() + path;
        return path;
    }

    public static Vector3 ReflectVelocity(Vector3 incoming, Vector3 normal,
        float bounce, float tangentRetention, float minimumSpeed)
    {
        float normalSpeed = Vector3.Dot(incoming, normal);
        if (normalSpeed >= 0f) return incoming;
        Vector3 tangent = incoming - normal * normalSpeed;
        float rebound = -normalSpeed * Mathf.Clamp01(bounce);
        if (rebound < minimumSpeed) rebound = 0f;
        return tangent * Mathf.Clamp01(tangentRetention) + normal * rebound;
    }

    private static Vector3 Quantize(Vector3 value) => new(
        Mathf.Round(value.x * Precision) / Precision,
        Mathf.Round(value.y * Precision) / Precision,
        Mathf.Round(value.z * Precision) / Precision);

    private void PushAndDamage(Collider other, Vector3 normal, float speed)
    {
        Rigidbody body = other.attachedRigidbody;
        if (body == null || body == rb) return;
        if (lastPushTimes.TryGetValue(body, out double last) && simulationTime - last < pushCooldown) return;
        lastPushTimes[body] = simulationTime;
        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        CatchableObj item = other.GetComponentInParent<CatchableObj>();
        if (player != null)
        {
            // The existing player movement channel will carry the local player's response.
            if (PlayerSpawnManager.Instance == null || !PlayerSpawnManager.Instance.IsMine(player.PlayerId)) return;
            GetComponent<IngredientDamageController>()?.ApplyScriptedImpact(player, speed);
            if (body.isKinematic || speed < minBounceSpeed) return;
            player.ActionController.Movement.BeginForcedMovement(0.12f);
            body.AddForce(-normal * Mathf.Min(speed * pushMultiplier, maxPushForce), ForceMode.Impulse);
        }
        else if (item != null && item != source && !item.IsHold && item.ParentEntityId <= 0 && speed >= minBounceSpeed)
        {
            Vector3 impulse = -normal * Mathf.Min(speed * pushMultiplier, maxPushForce);
            if (item.TryGetComponent(out IngredientTraitBounce bounce) && bounce.IsSimulating)
                bounce.velocity = Quantize(bounce.velocity + impulse / Mathf.Max(0.01f, body.mass));
            else if (!body.isKinematic) body.AddForce(impulse, ForceMode.Impulse);
        }
    }
}

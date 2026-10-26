using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    #region Inspector References

    [field: Header("Core")]
    [field: SerializeField] public Collider Col { get; private set; } = null;
    [field: SerializeField] public Rigidbody Rb { get; private set; } = null;

    [field: Header("Systems")]
    [field: SerializeField] public PlayerInputHandler Input { get; private set; } = null;
    [field: SerializeField] public PlayerCameraController CameraController { get; private set; } = null;
    [field: SerializeField] public PlayerEffectController EffectController { get; private set; } = null;
    [field: SerializeField] public PlayerGlow Glow { get; private set; } = null;

    [field: Header("Animated Body")]
    [field: SerializeField] public Transform FacingRoot { get; private set; }
    [field: SerializeField] public Animator Animator { get; private set; } = null;

    [field: Header("Camera Settings")]
    [field: SerializeField] public Transform CameraFollowTarget { get; private set; } = null;
    [field: SerializeField] public Transform CameraLookAtTarget { get; private set; } = null;

    [field: Header("Attachment Points")]
    [field: SerializeField] public Transform ItemPoint { get; private set; } = null;
    [field: SerializeField] public Transform EquipPoint { get; private set; } = null;
    [field: SerializeField] public GameObject ThrowPoint { get; private set; } = null;

    [field: Header("Ragdoll")]
    [field: SerializeField] public List<BodyPart> BodyParts { get; private set; } = new();

    #endregion

    #region Inspector Settings

    [field: Header("Network")]
    [field: SerializeField] public string PlayerId { get; set; } = null;

    [field: Header("Base Move")]
    [field: SerializeField] public float MoveSpeed { get; private set; }
    [field: SerializeField] public float RunMultiplier { get; private set; }
    [field: SerializeField, Min(0f)] public float Acceleration { get; private set; }
    [field: SerializeField, Min(0f)] public float Deceleration { get; private set; }

    [field: Header("Jump")]
    [field: SerializeField] public LayerMask GroundLayerMask { get; private set; } = ~0;
    [field: SerializeField] public float JumpPower { get; private set; } = 5.5f;
    [field: SerializeField, Min(1f)] public float FallMultiplier { get; private set; } = 2.5f;
    [field: SerializeField, Min(0f)] public float CoyoteTime { get; private set; } = 0.1f;
    [field: SerializeField, Min(0f)] public float JumpRepeatDelay { get; private set; } = 0.2f;

    [field: Header("Jump Validation")]
    [field: SerializeField] public float GroundCheckDistance { get; private set; } = 0.08f;
    [field: SerializeField, Min(0f)] public float FallCheckDistance { get; private set; } = 0.3f;
    [field: SerializeField] public bool DebugGroundCheck { get; private set; } = false;

    [field: Header("Action")]
    [field: SerializeField, Min(0f)] public float RecoveryDelay { get; private set; } = 0.1f;

    [field: Header("Interaction")]
    [field: SerializeField] public PhysicsCastProbe[] InteractionCasts { get; private set; } = System.Array.Empty<PhysicsCastProbe>();
    [field: SerializeField] public PhysicsCastProbe CameraInteractionCast { get; private set; }
    [field: SerializeField, HideInInspector] public LayerMask InteractLayerMask { get; private set; } = (1 << 7) | (1 << 10);
    [field: SerializeField, HideInInspector] public Vector3 InteractRayStartOffset { get; private set; } = new(0f, 0f, 0.3f);
    [field: SerializeField, HideInInspector, Min(0.01f)] public float InteractDistance { get; private set; } = 3.0f;
    [field: SerializeField, HideInInspector] public float InteractRadius { get; private set; } = 0.35f;
    [field: SerializeField, HideInInspector] public bool DebugInteraction { get; private set; } = false;

    [field: Header("Throw")]
    [field: SerializeField] public float ThrowForce { get; private set; } = 8.0f;
    [field: SerializeField] public float ThrowAngle { get; private set; } = 0.0f;
    [field: SerializeField, Min(0.1f)] public float ThrowMaxDistance { get; private set; } = 15f;
    [field: SerializeField] public Material ThrowTrajectoryMaterial { get; private set; }

    [field: Header("Health")]
    [field: SerializeField, Min(0f)] public float MaxHealth { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float DamageCooldown { get; private set; } = 0.2f;
    [field: SerializeField, Min(0f)] public float RagdollStunDuration { get; private set; } = 2f;

    #endregion

    #region Runtime State

    private PlayerActionController actionController = null;
    private PlayerInteract interact;
    private PlayerStateResolver stateResolver = null;

    private Camera playerCamera = null;
    private CinemachineCamera virtualCamera = null;
    private bool isInitialized = false;
    private PlayerRocketController rocket;

    #endregion

    #region Properties
    public PlayerActionController ActionController => actionController;
    public PlayerInteract Interact => interact;
    public PlayerThrowTracker ThrowTrajectory { get; private set; }
    public PlayerStateResolver StateResolver => stateResolver;
    public Camera PlayerCam => playerCamera;
    public PlayerHealthData Health { get; private set; }
    public PlayerRocketController Rocket
    {
        get
        {
            if (rocket == null)
            {
                rocket = GetComponent<PlayerRocketController>();
                if (rocket == null && Application.isPlaying)
                    rocket = gameObject.AddComponent<PlayerRocketController>();
            }
            if (rocket != null) rocket.Initialize(this);
            return rocket;
        }
    }
    #endregion

    #region Unity Callbacks

    private void Awake()
    {
        if (Glow != null)
            Glow.enabled = false;

        actionController = new PlayerActionController(this);
        EnsureInteractionCasts();
        interact = new PlayerInteract(this);
        ThrowTrajectory = new PlayerThrowTracker(this);
        _ = Rocket;
    }

    private void Update()
    {
        if (!isInitialized) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Temporary ragdoll test: remove this block and PlayerHealthData.DebugDepleteHealth together.
        if (stateResolver is LocalPlayerStateResolver &&
            UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.pKey.wasPressedThisFrame)
        {
            Health.DebugDepleteHealth();
        }
#endif

        stateResolver.UpdateTick();
        interact.Handle(stateResolver.CurrentState.Interaction);
        actionController.UpdateTick(stateResolver.CurrentState);
    }

    private void LateUpdate()
    {
        if (isInitialized) interact.UpdateThrowPreparation();
    }

    private void OnDisable()
    {
        interact?.CancelThrowPreparation();
    }

    private void FixedUpdate()
    {
        if (!isInitialized) return;

        stateResolver.FixedTick();
        actionController.FixedTick(stateResolver.CurrentState);
    }

    private void OnDestroy()
    {
        ThrowTrajectory?.Dispose();
        if (Health != null)
        {
            Health.HealthChanged -= OnHealthChanged;
        }

        if (PlayerSpawnManager.Instance == null) return;

        PlayerSpawnManager.Instance.UnregisterPlayer(this);
    }

    #endregion

    public void Initialize(string id)
    {
        Rocket?.StopFlight();
        PlayerId = id;

        if (Health != null)
        {
            Health.HealthChanged -= OnHealthChanged;
        }

        Health = new PlayerHealthData(MaxHealth, DamageCooldown);
        Health.HealthChanged += OnHealthChanged;

        bool isMine = PlayerSpawnManager.Instance.IsMine(PlayerId);
        SetLocalControlActive(isMine);

        stateResolver = isMine
            ? new LocalPlayerStateResolver(this)
            : new RemotePlayerStateResolver(this);

        PlayerSpawnManager.Instance.RegisterPlayer(this);

        isInitialized = true;
    }

    public void BindCamera(Camera cam, CinemachineCamera virtualCam)
    {
        playerCamera = cam;
        if (CameraInteractionCast != null)
            CameraInteractionCast.Origin = cam != null ? cam.transform : transform;
        virtualCamera = virtualCam;
        virtualCamera.Target.TrackingTarget = CameraFollowTarget;
        virtualCamera.Target.LookAtTarget = CameraLookAtTarget;
    }

    private void EnsureInteractionCasts()
    {
        // Older scenes without probe references retain their serialized aiming settings.
        if (InteractionCasts != null && InteractionCasts.Length > 0) return;

        PhysicsCastProbe nearCast = gameObject.AddComponent<PhysicsCastProbe>();
        nearCast.Origin = FacingRoot != null ? FacingRoot : transform;
        nearCast.OriginCollider = Col;
        nearCast.Distance = 0.5f;
        nearCast.LayerMask = InteractLayerMask;
        nearCast.DebugCast = DebugInteraction;
        nearCast.DebugColor = Color.green;

        CameraInteractionCast = gameObject.AddComponent<PhysicsCastProbe>();
        CameraInteractionCast.Type = PhysicsCastProbe.CastType.SphereCast;
        CameraInteractionCast.Origin = playerCamera != null ? playerCamera.transform : transform;
        CameraInteractionCast.StartOffset = InteractRayStartOffset;
        CameraInteractionCast.Distance = InteractDistance;
        CameraInteractionCast.Radius = InteractRadius;
        CameraInteractionCast.LayerMask = InteractLayerMask;
        CameraInteractionCast.DebugCast = DebugInteraction;
        InteractionCasts = new[] { nearCast, CameraInteractionCast };
    }

    private void SetLocalControlActive(bool isMine)
    {
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(isMine);
        }

        if (Input != null)
        {
            Input.enabled = isMine;
        }

        if (CameraController != null)
        {
            CameraController.SetLocalControlActive(isMine);
        }

        if (EffectController == null)
        {
            EffectController = GetComponent<PlayerEffectController>();
        }

    }

    private void OnHealthChanged(PlayerHealthData health)
    {
        if (!health.IsRagdoll)
        {
            return;
        }

        Rocket?.StopFlight();

        if (stateResolver is LocalPlayerStateResolver localStateResolver)
        {
            localStateResolver.EnterRagdoll();
        }
    }
}

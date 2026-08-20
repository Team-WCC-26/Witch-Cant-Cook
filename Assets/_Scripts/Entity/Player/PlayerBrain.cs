using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    [field: Header("Network")]
    [field: SerializeField] public string PlayerId { get; set; } = null;

    [field: Header("Core")]
    [field: SerializeField] public Collider Col { get; private set; } = null;
    [field: SerializeField] public Rigidbody Rb { get; private set; } = null;

    [field: Header("Health")]
    [field: SerializeField, Min(0f)] public float MaxHealth { get; private set; } = 100f;
    [field: SerializeField, Min(0f)] public float DamageCooldown { get; private set; } = 0.2f;
    [field: SerializeField, Min(0f)] public float RagdollStunDuration { get; private set; } = 2f;

    [field: Header("Camera Settings")]
    [field: SerializeField] public Transform CameraFollowTarget { get; private set; } = null;
    [field: SerializeField] public Transform CameraLookAtTarget { get; private set; } = null;

    [field: Header("Ragdoll")]
    [field: SerializeField] public List<BodyPart> BodyParts { get; private set; } = new();

    [field: Header("Systems")]
    [field: SerializeField] public PlayerInputHandler Input { get; private set; } = null;
    [field: SerializeField] public PlayerCameraController CameraController { get; private set; } = null;
    [field: SerializeField] public PlayerEffectController EffectController { get; private set; } = null;
    

    [field: Header("Animated Body")]
    [field: SerializeField] public Animator Animator { get; private set; } = null;

    [field: Header("Interaction")]
    [field: SerializeField] public Transform ItemPoint { get; private set; } = null;
    [field: SerializeField] public Transform EquipPoint { get; private set; } = null;
    [field: SerializeField] public Vector3 InteractRayStartOffset { get; private set; } = new(0f, 0f, 0.3f);
    [field: SerializeField] public float InteractDistance { get; private set; } = 3.0f;
    [field: SerializeField] public float InteractRadius { get; private set; } = 0.35f;
    [field: SerializeField] public bool DebugInteraction { get; private set; } = false;

    [field: Header("Base Move")]
    [field: SerializeField] public float MoveSpeed { get; private set; }
    [field: SerializeField] public float RunMultiplier { get; private set; } 
    [field: SerializeField, Min(0f)] public float Acceleration { get; private set; } 
    [field: SerializeField, Min(0f)] public float Deceleration { get; private set; }

    [field: Header("Action")]
    [field: SerializeField, Min(0f)] public float RecoveryDelay { get; private set; } = 0.1f;

    [field: Header("Jump")]
    [field: SerializeField] public LayerMask GroundLayerMask { get; private set; } = ~0;
    [field: SerializeField] public float JumpPower { get; private set; } = 5.5f;
    [field: SerializeField, Min(1f)] public float FallMultiplier { get; private set; } = 2.5f;
    [field: SerializeField, Min(0f)] public float CoyoteTime { get; private set; } = 0.1f;

    [field: Header("Jump Validation")]
    [field: SerializeField] public float GroundCheckDistance { get; private set; } = 0.08f;
    [field: SerializeField] public bool DebugGroundCheck { get; private set; } = false;

    [field: Header("Throw")]
    [field: SerializeField] public float ThrowForce { get; private set; } = 8.0f;
    [field: SerializeField] public float ThrowAngle { get; private set; } = 0.0f;
    [field: SerializeField] public Vector3 ThrowCameraOffset { get; private set; } = Vector3.zero;
    private PlayerInteract interact;

    private PlayerStateResolver stateResolver = null;
    private PlayerActionController actionController = null;
    private bool isInitialized = false;

    //cameras
    private Camera playerCamera = null;
    private CinemachineCamera virtualCamera = null;

    #region properties
    public Camera PlayerCam => playerCamera;
    public PlayerInteract Interact => interact;
    public PlayerStateResolver StateResolver => stateResolver;
    public PlayerActionController ActionController => actionController;
    public PlayerHealthData Health { get; private set; }
    #endregion

    private void Awake()
    {
        actionController = new PlayerActionController(this);
        interact = new PlayerInteract(this);
    }

    public void Initialize(string id)
    {
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

    private void Update()
    {
        if (!isInitialized) return;

        stateResolver.UpdateTick();
        interact.Handle(stateResolver.CurrentState.Interaction);
        actionController.UpdateTick(stateResolver.CurrentState);
    }

    private void FixedUpdate()
    {
        if (!isInitialized) return;

        stateResolver.FixedTick();
        actionController.FixedTick(stateResolver.CurrentState);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!isInitialized) return;
        if (!PlayerSpawnManager.Instance.IsMine(PlayerId)) return;
        if (stateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default) return;

        stateResolver.NotifyCollision(collision);
    }

    private void OnDestroy()
    {
        if (Health != null)
        {
            Health.HealthChanged -= OnHealthChanged;
        }

        if (PlayerSpawnManager.Instance == null) return;

        PlayerSpawnManager.Instance.UnregisterPlayer(this);
    }

    public void BindCamera(Camera cam, CinemachineCamera virtualCam)
    {
        playerCamera = cam;
        virtualCamera = virtualCam;
        virtualCamera.Target.TrackingTarget = CameraFollowTarget;
        virtualCamera.Target.LookAtTarget = CameraLookAtTarget;
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

        if (stateResolver is LocalPlayerStateResolver localStateResolver)
        {
            localStateResolver.EnterRagdoll();
        }
    }
}

using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] private string playerId = null;

    [Header("Core")]
    [SerializeField] private Collider col = null;
    [SerializeField] private Rigidbody rb = null;

    [Header("Camera Settings")]
    [SerializeField] private Transform cameraFollowTarget = null;
    [SerializeField] private Transform cameraLookAtTarget = null;

    [Header("Ragdoll")]
    [SerializeField] private List<BodyPart> bodyParts = new();

    [Header("Systems")]
    [SerializeField] private PlayerInputHandler input = null;
    [SerializeField] private PlayerCameraController camController = null;

    [Header("Animated Body")]
    [SerializeField] private Animator animator = null;

    [Header("Interaction")]
    [SerializeField] private Transform itemHoldParent = null;
    [SerializeField] private float interactRayStartOffset = 0.3f;
    [SerializeField] private float interactDistance = 3.0f;
    [SerializeField] private float interactRadius = 0.35f;

    [Header("Throw")]
    [SerializeField] private float throwForce = 8.0f;
    [SerializeField] private float throwAngle = 0.0f;
    [SerializeField] private Vector3 throwCameraOffset = Vector3.zero;
    private PlayerInteract interact;

    private PlayerStateResolver stateResolver = null;
    private PlayerActionController actionController = null;
    private bool isInitialized = false;

    //cameras
    private Camera playerCamera = null;
    private CinemachineCamera virtualCamera = null;

    #region properties
    public string PlayerId
    {
        get => playerId;
        set => playerId = value;
    }
    public Camera PlayerCam => playerCamera;
    public Collider Col => col;
    public Rigidbody Rb => rb;
    public IReadOnlyList<BodyPart> BodyParts => bodyParts;
    public PlayerInputHandler Input => input;
    public PlayerActionController ActionController => actionController;
    public PlayerCameraController CameraController => camController;
    public Animator Animator => animator;
    public Transform ItemHoldParent => itemHoldParent;
    public float InteractRayStartOffset => interactRayStartOffset;
    public float InteractDistance => interactDistance;
    public float InteractRadius => interactRadius;
    public float ThrowForce => throwForce;
    public float ThrowAngle => throwAngle;
    public Vector3 ThrowCameraOffset => throwCameraOffset;
    public PlayerInteract Interact => interact;
    public PlayerStateResolver StateResolver => stateResolver;
    #endregion

    private void Awake()
    {
        actionController = new PlayerActionController(this);
        interact = new PlayerInteract(this);
    }

    public void Initialize(string id)
    {
        playerId = id;

        bool isMine = PlayerSpawnManager.Instance.IsMine(playerId);
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
        if (!PlayerSpawnManager.Instance.IsMine(playerId)) return;
        if (stateResolver.CurrentState.PhysicalMode != PlayerPhysicalMode.Default) return;

        stateResolver.NotifyCollision(collision);
    }

    private void OnDestroy()
    {
        if (PlayerSpawnManager.Instance == null) return;

        PlayerSpawnManager.Instance.UnregisterPlayer(this);
    }

    public void BindCamera(Camera cam, CinemachineCamera virtualCam)
    {
        playerCamera = cam;
        virtualCamera = virtualCam;
        virtualCamera.Target.TrackingTarget = cameraFollowTarget;
        virtualCamera.Target.LookAtTarget = cameraLookAtTarget;
    }

    private void SetLocalControlActive(bool isMine)
    {
        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(isMine);
        }

        if (input != null)
        {
            input.enabled = isMine;
        }

        if (camController != null)
        {
            camController.SetLocalControlActive(isMine);
        }
    }
}

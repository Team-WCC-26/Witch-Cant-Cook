using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    [Header("Network")]
    [SerializeField] private string playerId = null;

    [Header("Core")]
    [SerializeField] private Camera playerCamera = null;
    [SerializeField] private Collider col = null;
    [SerializeField] private Rigidbody rb = null;

    [Header("Ragdoll")]
    [SerializeField] private List<BodyPart> bodyParts = new();

    [Header("Systems")]
    [SerializeField] private PlayerInputHandler input = null;
    [SerializeField] private PlayerCameraController camController = null;

    [Header("Animated Body")]
    [SerializeField] private Animator animator = null;

    private PlayerStateResolver stateResolver = null;
    private PlayerActionController actionController = null;
    private bool isInitialized = false;

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

    public PlayerStateResolver StateResolver => stateResolver;
    #endregion

    private void Awake()
    {
        actionController = new PlayerActionController(this);
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
            camController.enabled = isMine;
        }
    }
}
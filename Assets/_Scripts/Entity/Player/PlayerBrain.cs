using MemoryPack;
using Protocol;
using Server;
using System;
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

    private PlayerStateResolver stateResolver = null;
    private PlayerActionController actionController = null;
    private bool isInitialized = false;

    //Packet Ids
    private PacketId _joinMemberID => PacketId.S_PlayerEnter;
    private PacketId _worldStateID => PacketId.S_WorldState;

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

    private void OnEnable()
    {
        ServerManager.Instance.RegisterHandler(_joinMemberID, MemberJoined);
        ServerManager.Instance.RegisterHandler(_worldStateID, WorldStateReceived);
    }

    private void OnDisable()
    {
        ServerManager.Instance.UnRegisterHandler(_joinMemberID);
        ServerManager.Instance.UnRegisterHandler(_worldStateID);
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

    public void BindCamera(Camera cam, CinemachineCamera virtualCam)
    {
        playerCamera = cam;
        virtualCamera = virtualCam;
        virtualCamera.Target.TrackingTarget = cameraFollowTarget;
        virtualCamera.Target.LookAtTarget = cameraLookAtTarget;
    }

    private void MemberJoined(ReadOnlyMemory<byte> data)
    {
        var packet = MemoryPackSerializer.Deserialize<PlayerEnterPacket>(data.Span);
        string playerId = packet.NewPlayerID;

        if (!PlayerSpawnManager.Instance.ContainsPlayer(playerId))
            PlayerSpawnManager.Instance.SpawnPlayer(playerId);

        UIManager.Hide<LobbyRouterUI>();
    }

    private void WorldStateReceived(ReadOnlyMemory<byte> data)
    {
        if (PlayerSpawnManager.Instance.IsMine(playerId)) return;
        
        var packet = MemoryPackSerializer.Deserialize<WorldStatePacket>(data.Span);
        stateResolver.ApplyRemotePacket(packet);
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
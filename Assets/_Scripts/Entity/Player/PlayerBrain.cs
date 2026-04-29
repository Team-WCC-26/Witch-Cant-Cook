using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
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

    #region properties
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
        stateResolver = new PlayerStateResolver(this);
        actionController = new PlayerActionController(this);
    }

    private void Update()
    {
        stateResolver.UpdateTick();
        actionController.UpdateTick(stateResolver.CurrentState);
    }

    private void FixedUpdate()
    {
        stateResolver.FixedTick();
        actionController.FixedTick(stateResolver.CurrentState);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (stateResolver.CurrentState.PhysicalMode == PlayerPhysicalMode.Default)
        {
            stateResolver.NotifyCollision(collision);
        }
    }
}
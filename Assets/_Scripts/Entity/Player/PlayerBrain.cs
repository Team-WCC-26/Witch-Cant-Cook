using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private Camera playerCamera = null;
    [SerializeField] private Collider col = null;
    [SerializeField] private Rigidbody rb = null;

    [Header("Systems")]
    [SerializeField] private PlayerInputHandler input = null;
    [SerializeField] private PlayerMovement movement = null;
    [SerializeField] private PlayerLocomotion locomotion = null;
    [SerializeField] private PlayerCameraController camController = null;
    [SerializeField] private PlayerInteract interact = null;

    [Header("Animated Body")]
    [SerializeField] private Animator animator = null;

    public Camera PlayerCam => playerCamera;
    public Collider Col => col;
    public Rigidbody Rb => rb;

    public PlayerInputHandler Input => input;
    public PlayerMovement Movement => movement;
    public PlayerLocomotion Locomotion => locomotion;
    public PlayerCameraController CameraController => camController;
    public PlayerInteract Interact => interact;

    public Animator Animator => animator;
}
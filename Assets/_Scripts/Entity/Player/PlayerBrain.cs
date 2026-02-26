using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerBrain : MonoBehaviour
{
    [SerializeField] private bool autoFindOnAwake = true;

    [Header("Refs")]
    [SerializeField] private Collider col = null;
    [SerializeField] private Rigidbody rb = null;
    [SerializeField] private PlayerInputHandler input = null;
    [SerializeField] private PlayerMovement movement = null;
    [SerializeField] private LowerBodyController lowerBody = null;
    [SerializeField] private UpperBodyController upperBody = null;
    [SerializeField] private PlayerCameraController camController = null;

    public Collider Col => col;
    public Rigidbody Rb => rb;
    public PlayerInputHandler Input => input;
    public PlayerMovement Movement => movement;
    public PlayerCameraController CameraController => camController;
    public LowerBodyController LowerBody => lowerBody;
    public UpperBodyController UpperBody => upperBody;

    private void Awake()
    {
        if (!autoFindOnAwake) return;

        if (input == null) input = GetComponent<PlayerInputHandler>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (lowerBody == null) lowerBody = GetComponent<LowerBodyController>();
        if (upperBody == null) upperBody = GetComponent<UpperBodyController>();
    }

    //private void Update()
    //{
    //    if (!IsWired()) return;

    //    // 1) 입력 읽기 (InputHandler는 입력만 제공)
    //    Vector2 moveInput = input.Move;
    //    bool isMovePressed = moveInput.sqrMagnitude > 0.0001f;

    //    // 2) 하체 애니메이션 파라미터 반영 (Animator만)
    //    lowerBody.SetMoveInput(moveInput);
    //    lowerBody.SetMoving(isMovePressed);

    //    // 3) 상체 흔들림 게이트(속도 기반)
    //    // 이동 속도는 물리 결과를 기준으로 하는 게 안정적
    //    Vector3 vel = movement.Velocity;
    //    float planarSpeed = new Vector2(vel.x, vel.z).magnitude;

    //    float wobbleGate = (planarSpeed >= wobbleGateSpeedThreshold) ? 1f : 0f;
    //    upperBody.SetWobbleGate(wobbleGate);

    //    // 상체가 속도 크기에 따라 흔들림 강도를 바꾸고 싶으면
    //    // upperBody.SetSpeed(planarSpeed); 같은 API를 두고 여기서 전달
    //}

    //private void FixedUpdate()
    //{
    //    if (!IsWired()) return;

    //    // 4) 이동 실행 (Rigidbody는 FixedUpdate에서)
    //    movement.SetMoveInput(input.Move);
    //}

    //private bool IsWired()
    //{
    //    return input != null && movement != null && lowerBody != null && upperBody != null;
    //}
}
using UnityEngine;

public class PlayerMovement
{
    private readonly PlayerBrain brain;



    #region Movement Settings
    private readonly float moveSpeed;
    private readonly float runMultiplier;
    private readonly float jumpPower;

    //baseFrictionAccel 여기에 먹힘 : 가속도와 감속도
    private readonly float acceleration;
    private readonly float deceleration;
    #endregion

    private Rigidbody rb => brain.Rb;

    public float CurrentSpeed { get; private set; }
    public bool IsGroundedNow => IsGrounded();
    public float VerticalSpeed => rb.linearVelocity.y;
    public float SpeedMultiplier { get; set; } = 1f;

    /// <summary>
    /// 목표 속도로 수렴하는 빠르기를 조절하는 마찰 계수.
    /// 1(기본값)이면 사실상 즉시 목표 속도에 도달하고(기존 동작과 동일),
    /// 값이 낮을수록 관성이 남아 서서히 수렴한다(빙판 등에서 미끄러지는 느낌).
    /// </summary>
    public float FrictionMultiplier { get; set; } = 1f;

    private float lastGroundedTime = float.NegativeInfinity;
    public bool CanJump =>
        IsGroundedNow ||
        Time.time - lastGroundedTime <= brain.CoyoteTime;

    public PlayerMovement(PlayerBrain brain, float moveSpeed, float runMultiplier, float jumpPower, float acceleration, float deceleration)
    {
        this.brain = brain;
        this.moveSpeed = moveSpeed;
        this.runMultiplier = runMultiplier;
        this.jumpPower = jumpPower;
        this.acceleration = acceleration;
        this.deceleration = deceleration;
    }

    public void Move(Vector2 moveInput, bool isRun)
    {
        Vector3 moveDir =
            brain.transform.right * moveInput.x +
            brain.transform.forward * moveInput.y;

        if (moveDir.sqrMagnitude > 1f)
        {
            moveDir.Normalize();
        }

        float speed = (isRun ? moveSpeed * runMultiplier : moveSpeed) * SpeedMultiplier;
        Vector3 targetVelocity = moveDir * speed;

        Vector3 currentHorizontal = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);

        // 목표 속도가 현재보다 커지는 "가속" 상황은 항상 즉각 반응하게 두고(입력이 먹통처럼 느껴지지 않게),
        // 목표 속도가 현재보다 작아지는 "감속/정지" 상황에서만 FrictionMultiplier를 적용해
        // 관성이 남아 미끄러지는 느낌을 낸다. (빙판: 밀 때는 평소처럼, 멈추거나 방향을 바꿀 때만 미끄러짐)
        bool hasMoveInput = moveDir.sqrMagnitude > 0.0001f;
        float targetSpeed = hasMoveInput ? targetVelocity.magnitude : 0f;
        float currentSpeed = CurrentSpeed;
        float velocityDelta = targetSpeed > currentSpeed ? acceleration : deceleration * FrictionMultiplier;
        float newSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, velocityDelta * Time.fixedDeltaTime);
        Vector3 velocityDirection = hasMoveInput ? moveDir.normalized : currentHorizontal.normalized;
        Vector3 newHorizontal = velocityDirection * newSpeed;
        rb.linearVelocity = new Vector3(
            newHorizontal.x,
            rb.linearVelocity.y,
            newHorizontal.z
        );
        rb.angularVelocity = Vector3.zero;

        CurrentSpeed = newSpeed;
        Debug.Log($"newSpeed: {CurrentSpeed}, velocity: {rb.linearVelocity}");
    }

    public void Stop()
    {
        rb.linearVelocity = new Vector3(
            0f,
            rb.linearVelocity.y,
            0f
        );
        rb.angularVelocity = Vector3.zero;

        CurrentSpeed = 0f;
    }

    #region Jump
    public void Jump()
    {
        if (!CanJump) return;

        Vector3 velocity = rb.linearVelocity;
        velocity.y = 0f;
        rb.linearVelocity = velocity;

        rb.AddForce(Vector3.up * jumpPower, ForceMode.Impulse);
    }

    public void ApplyFallGravity()
    {
        if (rb.linearVelocity.y >= 0f) return;

        rb.AddForce(
            Physics.gravity * (brain.FallMultiplier - 1f),
            ForceMode.Acceleration
        );
    }

    private bool IsGrounded()
    {
        if (brain.Col == null) return false;

        Bounds bounds = brain.Col.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f;
        float distance = bounds.extents.y - radius + brain.GroundCheckDistance;

        if (brain.DebugGroundCheck)
        {
            Debug.DrawRay(
                bounds.center,
                Vector3.down * (distance + radius),
                Color.green
            );
        }

        return Physics.SphereCast(
            bounds.center,
            radius,
            Vector3.down,
            out _,
            distance,
            brain.GroundLayerMask,
            QueryTriggerInteraction.Ignore
        );
    }

    public void UpdateGroundState()
    {
        if (IsGrounded())
            lastGroundedTime = Time.time;
    }
    #endregion
}
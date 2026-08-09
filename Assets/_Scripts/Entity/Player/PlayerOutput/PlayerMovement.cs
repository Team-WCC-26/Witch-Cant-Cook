using UnityEngine;

public class PlayerMovement
{
    private readonly PlayerBrain brain;



    #region Movement Settings
    private readonly float moveSpeed;
    private readonly float runMultiplier;
    private readonly float jumpPower;
    #endregion

    private Rigidbody rb => brain.Rb;

    public float CurrentSpeed { get; private set; }
    public bool IsGroundedNow => IsGrounded();
    public float VerticalSpeed => rb.linearVelocity.y;
    public float SpeedMultiplier { get; set; } = 1f;

    private float lastGroundedTime = float.NegativeInfinity;
    public bool CanJump => 
        IsGroundedNow || 
        Time.time - lastGroundedTime <= brain.CoyoteTime;

    public PlayerMovement(PlayerBrain brain, float moveSpeed, float runMultiplier, float jumpPower)
    {
        this.brain = brain;
        this.moveSpeed = moveSpeed;
        this.runMultiplier = runMultiplier;
        this.jumpPower = jumpPower;
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

        float speed = (isRun ? moveSpeed * runMultiplier : moveSpeed)  * SpeedMultiplier;
        Vector3 velocity = moveDir * speed;

        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );
        rb.angularVelocity = Vector3.zero;

        CurrentSpeed = velocity.magnitude;
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

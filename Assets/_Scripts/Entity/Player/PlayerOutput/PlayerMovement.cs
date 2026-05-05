using UnityEngine;

public class PlayerMovement
{
    private readonly PlayerBrain brain;

    private readonly float moveSpeed = 5.0f;
    private readonly float runMultiplier = 1.5f;

    private Rigidbody rb => brain.Rb;

    public float CurrentSpeed { get; private set; }

    public PlayerMovement(PlayerBrain brain)
    {
        this.brain = brain;
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

        float speed = isRun ? moveSpeed * runMultiplier : moveSpeed;
        Vector3 velocity = moveDir * speed;

        rb.linearVelocity = new Vector3(
            velocity.x,
            rb.linearVelocity.y,
            velocity.z
        );

        CurrentSpeed = velocity.magnitude;
    }

    public void Stop()
    {
        rb.linearVelocity = new Vector3(
            0f,
            rb.linearVelocity.y,
            0f
        );

        CurrentSpeed = 0f;
    }
}
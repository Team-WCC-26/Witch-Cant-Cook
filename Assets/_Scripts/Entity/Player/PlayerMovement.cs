using UnityEngine;

public sealed class PlayerMovement : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerBrain brain;   // 반드시 Inspector에서 할당

    [Header("Tuning")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float acceleration = 40.0f;

    private Rigidbody rb => brain.Rb;
    private PlayerInputHandler input => brain.Input;

    public float CurrentSpeed { get; private set; }


    private void FixedUpdate()
    {
        Vector2 moveInput = input.Move;

        Vector3 desiredDir =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        if (desiredDir.sqrMagnitude > 1f)
            desiredDir.Normalize();

        Vector3 desiredVelocity = desiredDir * moveSpeed;

        Vector3 currentVel = rb.linearVelocity;
        Vector3 currentPlanar = new Vector3(currentVel.x, 0f, currentVel.z);

        float k = 1f - Mathf.Exp(-acceleration * Time.fixedDeltaTime);
        Vector3 newPlanar = Vector3.Lerp(currentPlanar, desiredVelocity, k);

        rb.linearVelocity = new Vector3(newPlanar.x, currentVel.y, newPlanar.z);

        CurrentSpeed = newPlanar.magnitude;
    }
}
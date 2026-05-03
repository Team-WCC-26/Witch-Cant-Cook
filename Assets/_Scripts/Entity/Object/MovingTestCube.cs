using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class MovingTestCube : MonoBehaviour
{
    [SerializeField] private Vector3 moveDirection = Vector3.forward;
    [SerializeField] private float moveDistance = 1.5f;
    [SerializeField] private float moveSpeed = 1.2f;

    private Rigidbody rb;
    private Vector3 startPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        startPosition = transform.position;

        if (moveDirection.sqrMagnitude > 0.0001f)
        {
            moveDirection.Normalize();
        }
    }

    private void FixedUpdate()
    {
        float offset = Mathf.Sin(Time.time * moveSpeed) * moveDistance;
        Vector3 targetPosition = startPosition + moveDirection * offset;

        rb.MovePosition(targetPosition);
    }
}
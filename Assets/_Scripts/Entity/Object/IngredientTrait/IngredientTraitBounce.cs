using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class IngredientTraitBounce : MonoBehaviour
{
    [SerializeField] private Rigidbody rb;

    [Header("Push")]
    [SerializeField] private float pushMultiplier = 0.5f;
    [SerializeField] private float maxPushForce = 8f;
    [SerializeField] private float minBounceSpeed = 2f;

    [Header("Movement")]
    [SerializeField] private float minSpeed = 5f;

    private void Reset()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        Rigidbody otherRb = collision.rigidbody;

        if (otherRb == null || otherRb == rb || otherRb.isKinematic)
            return;

        // ≈ ≈ ∫º ¿⁄Ω≈¿« º”µµ ±‚¡ÿ
        float speed = rb.linearVelocity.magnitude;

        if (speed < minBounceSpeed)
            return;

        Vector3 dir = (otherRb.worldCenterOfMass - rb.worldCenterOfMass).normalized;

        float force = Mathf.Min(speed * pushMultiplier, maxPushForce);

        otherRb.AddForce(dir * force, ForceMode.Impulse);
    }

    private void FixedUpdate()
    {
        if (rb.linearVelocity.magnitude < minSpeed)
        {
            rb.linearVelocity =
                rb.linearVelocity.normalized * minSpeed;
        }
    }
}
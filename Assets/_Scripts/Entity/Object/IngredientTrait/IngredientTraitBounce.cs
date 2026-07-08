using UnityEngine;

public class IngredientTraitBounce : MonoBehaviour
{
    [SerializeField] PhysicsMaterial material;
    [SerializeField] float bounciness = 0.9f;
    [SerializeField] float pushMultiplier = 0.8f;

    [SerializeField] private Rigidbody rb;

    [SerializeField] private float minSpeed = 5f;

    void Awake()
    {
        material.bounciness = bounciness;
    }

    void OnCollisionEnter(Collision collision)
    {
        Rigidbody rb = collision.rigidbody;

        if (rb == null || rb.isKinematic)
            return;

        Vector3 dir = (rb.position - transform.position).normalized;

        float force = collision.relativeVelocity.magnitude * pushMultiplier;

        rb.AddForce(dir * force, ForceMode.Impulse);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (rb.linearVelocity.magnitude < minSpeed)
        {
            rb.linearVelocity =
                rb.linearVelocity.normalized * minSpeed;
        }
    }
}

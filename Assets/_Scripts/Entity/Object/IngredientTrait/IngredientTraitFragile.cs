using Protocol;
using System;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientTraitFragile : IngredientTrait
{
    [Header("References")]
    [SerializeField] private CatchableObj catchable;

    [Header("Break")]
    [SerializeField] private float breakImpactThreshold = 1f; // 충격량이 이 값 이상이면 깨짐

    [Header("Effect")]
    [SerializeField] private ParticleSystem breakEffect;

    public event Action<Vector3, Vector3> OnBroken;

    private bool isBroken;

    private void Reset()
    {
        catchable = GetComponent<CatchableObj>();
    }

    private void Awake()
    {
        if (catchable == null)
            catchable = GetComponent<CatchableObj>();
    }

    protected override void StartTrait()
    {
        isBroken = false;
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Only the last holder's client may request the replacement and destruction.
        // Check before latching isBroken so remote collisions cannot consume a later pickup.
        if (catchable == null || !catchable.IsLocalOwner)
            return;

        if (isBroken)
            return;

        if (collision.relativeVelocity.magnitude < breakImpactThreshold)
            return;

        if (collision.contactCount == 0) return;
        ContactPoint contact = collision.GetContact(0);
        Break(contact.point, contact.normal);
    }

    private void Break(Vector3 point, Vector3 normal)
    {
        isBroken = true;

        OnBroken?.Invoke(point, normal);
    }
}

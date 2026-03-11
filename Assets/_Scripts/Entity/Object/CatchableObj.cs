using UnityEngine;

public class CatchableObj : MonoBehaviour
{
    [SerializeField] private Collider col;
    [SerializeField] private Rigidbody rb;

    [Header("Obj Settings")]
    [SerializeField] private bool canBePicked = true;
    [SerializeField] private Vector3 holdLocalPosition = Vector3.zero;
    [SerializeField] private Vector3 holdLocalEulerAngles = Vector3.zero;
    [SerializeField] private float throwForce = 0;

    public Rigidbody Rb => rb;
    public bool CanBePicked => canBePicked;
    public Vector3 HoldLocalPosition => holdLocalPosition;
    public Vector3 HoldLocalEulerAngles => holdLocalEulerAngles;
    public float ThrowForce => throwForce;

    public bool IsHold { get; private set; } = false;

    public void OnCatch()
    {
        IsHold = true;
    }

    public void OnDrop()
    {
        IsHold = false;
    }

    public void OnThrow()
    {
        IsHold = false;
    }

    public void SetPhysicsState(bool enablePhysics)
    {
        if (rb == null) return;

        if (!enablePhysics)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        rb.isKinematic = !enablePhysics;
        rb.useGravity = enablePhysics;
    }
}
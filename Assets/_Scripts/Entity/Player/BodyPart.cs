using UnityEngine;

public enum BodyPartType
{
    Default,
    Pelvis,
    Spine,
    Head,

    UpperArmLeft,
    UpperArmRight,
    ForearmLeft,
    ForearmRight,
    HandLeft,
    HandRight,

    UpperLegLeft,
    UpperLegRight,
    LowerLegLeft,
    LowerLegRight,
    FootLeft,
    FootRight
}

public class BodyPart : MonoBehaviour
{
    [Header("Component Refs")]
    [SerializeField] private BodyPartType type;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider col;
    [SerializeField] private ConfigurableJoint joint;

    [Header("Animated Body")]
    [SerializeField] private Transform animatedBody;

    public BodyPartType Type => type;
    public Rigidbody Rb => rb;
    public Collider Col => col;
    public ConfigurableJoint Joint => joint;
    public Transform AnimatedBody => animatedBody;

    private void Awake()
    {
        InitializeRagdollState();
    }

    private void InitializeRagdollState()
    {
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.detectCollisions = true;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (col != null)
        {
            col.enabled = true;
        }
    }
}
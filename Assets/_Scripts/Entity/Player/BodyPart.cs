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
        InitializeRigidbody();
        InitializeCollider();
        InitializeJoint();
    }

    private void InitializeRigidbody()
    {
        if (rb == null)
        {
            return;
        }

        rb.isKinematic = true;
        rb.useGravity = false;
        rb.detectCollisions = true;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        rb.maxDepenetrationVelocity = 0.8f;
        rb.solverIterations = 8;
        rb.solverVelocityIterations = 4;

        rb.linearDamping = 0.05f;
        rb.angularDamping = 0.2f;
    }

    private void InitializeCollider()
    {
        if (col == null)
        {
            return;
        }

        col.enabled = true;
    }

    private void InitializeJoint()
    {
        if (joint == null)
        {
            return;
        }

        joint.enableCollision = false;
        joint.enablePreprocessing = false;
        joint.projectionMode = JointProjectionMode.None;

        JointDrive disabledDrive = new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 0f,
            maximumForce = 0f
        };

        joint.angularXDrive = disabledDrive;
        joint.angularYZDrive = disabledDrive;
        joint.slerpDrive = disabledDrive;
    }
}
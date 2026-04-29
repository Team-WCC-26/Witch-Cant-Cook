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

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.detectCollisions = true;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        rb.maxDepenetrationVelocity = 0.5f;
        rb.solverIterations = 12;
        rb.solverVelocityIterations = 8;

        rb.linearDamping = 0.25f;
        rb.angularDamping = 1.2f;
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
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.08f;
        joint.projectionAngle = 12f;

        JointDrive angularDrive = new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 8f,
            maximumForce = 80f
        };

        joint.angularXDrive = angularDrive;
        joint.angularYZDrive = angularDrive;

        joint.rotationDriveMode = RotationDriveMode.Slerp;

        JointDrive slerpDrive = new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 8f,
            maximumForce = 80f
        };

        joint.slerpDrive = slerpDrive;
    }
}
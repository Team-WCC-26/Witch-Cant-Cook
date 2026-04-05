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
}
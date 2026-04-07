using System.Collections.Generic;
using UnityEngine;

public class PlayerLocomotion : MonoBehaviour
{
    [SerializeField] private PlayerBrain brain = null;
    [SerializeField] private Transform yawRoot = null;

    [Header("Body Parts")]
    [SerializeField] private List<BodyPart> bodyParts = new();

    [Header("Adaptive Joint Drive")]
    [SerializeField] private float minSpring = 120f;
    [SerializeField] private float maxSpring = 1100f;
    [SerializeField] private float minDamper = 45f;
    [SerializeField] private float maxDamper = 110f;
    [SerializeField] private float minMaxForce = 250f;
    [SerializeField] private float maxMaxForce = 3500f;

    [Header("Error Mapping")]
    [SerializeField] private float lowErrorAngle = 3f;
    [SerializeField] private float highErrorAngle = 35f;
    [SerializeField] private float wakeUpErrorAngle = 8f;

    [Header("Optional")]
    [SerializeField] private bool forceSlerpDriveMode = true;
    [SerializeField] private bool wakeSleepingBodies = true;

    private readonly Dictionary<BodyPartType, BodyPart> bodyPartDict = new();
    public IReadOnlyDictionary<BodyPartType, BodyPart> BodyParts => bodyPartDict;

    private void Awake()
    {
        InitializeBodyPartDictionary();
        InitializeJointDriveMode();
    }

    private void FixedUpdate()
    {
        ApplyAnimatedPoseAndDrive();
    }

    private void InitializeBodyPartDictionary()
    {
        bodyPartDict.Clear();

        for (int i = 0; i < bodyParts.Count; i++)
        {
            BodyPart part = bodyParts[i];

            if (part == null)
                continue;

            if (part.Type == BodyPartType.Default)
            {
                Debug.LogWarning("BodyPartType.Default 는 실제 파츠로 사용하지 않는 것이 좋다.", part);
                continue;
            }

            if (bodyPartDict.ContainsKey(part.Type))
            {
                Debug.LogWarning($"중복 BodyPartType 감지: {part.Type}", part);
                continue;
            }

            bodyPartDict.Add(part.Type, part);
        }
    }

    private void InitializeJointDriveMode()
    {
        if (!forceSlerpDriveMode)
            return;

        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;

            if (part == null || part.Joint == null)
                continue;

            part.Joint.rotationDriveMode = RotationDriveMode.Slerp;
        }
    }

    private void ApplyAnimatedPoseAndDrive()
    {
        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;

            if (part == null)
                continue;

            ConfigurableJoint joint = part.Joint;
            Transform animatedBody = part.AnimatedBody;

            if (joint == null || animatedBody == null)
                continue;

            Quaternion targetLocalRotation = animatedBody.localRotation;
            joint.targetRotation = targetLocalRotation;

            float errorAngle = Quaternion.Angle(part.transform.localRotation, targetLocalRotation);
            float t = EvaluateNormalizedError(errorAngle);

            ApplyAdaptiveSlerpDrive(joint, t);

            if (wakeSleepingBodies && errorAngle >= wakeUpErrorAngle)
            {
                Rigidbody rb = part.GetComponent<Rigidbody>();

                if (rb != null && rb.IsSleeping())
                    rb.WakeUp();
            }
        }
    }

    private float EvaluateNormalizedError(float errorAngle)
    {
        if (highErrorAngle <= lowErrorAngle)
            return 1f;

        float t = Mathf.InverseLerp(lowErrorAngle, highErrorAngle, errorAngle);
        return Mathf.Clamp01(t);
    }

    private void ApplyAdaptiveSlerpDrive(ConfigurableJoint joint, float normalizedError)
    {
        JointDrive drive = joint.slerpDrive;

        float springT = Mathf.SmoothStep(0f, 1f, normalizedError);
        float damperT = 1f - normalizedError * 0.5f;

        drive.positionSpring = Mathf.Lerp(minSpring, maxSpring, springT);
        drive.positionDamper = Mathf.Lerp(minDamper, maxDamper, damperT);
        drive.maximumForce = Mathf.Lerp(minMaxForce, maxMaxForce, springT);

        joint.slerpDrive = drive;
    }

    public bool TryGetBodyPart(BodyPartType type, out BodyPart bodyPart)
    {
        return bodyPartDict.TryGetValue(type, out bodyPart);
    }
}
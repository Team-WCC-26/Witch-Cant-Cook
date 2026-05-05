using System.Collections.Generic;
using UnityEngine;

public class PlayerLocomotionLocalTest : MonoBehaviour
{
    [System.Serializable]
    private class DriveProfile
    {
        public BodyPartType type = BodyPartType.Default;
        public float spring = 1000f;
        public float damper = 100f;
        public float maxForce = 3000f;
    }

    [SerializeField] private List<BodyPart> bodyParts = new();

    [Header("Default Drive")]
    [SerializeField] private float defaultSpring = 1000f;
    [SerializeField] private float defaultDamper = 100f;
    [SerializeField] private float defaultMaxForce = 3000f;

    [Header("Per-Part Drive Override")]
    [SerializeField] private List<DriveProfile> driveProfiles = new();

    [Header("Debug")]
    [SerializeField] private float logInterval = 0.5f;
    [SerializeField] private float logErrorThreshold = 5f;

    private readonly Dictionary<BodyPartType, BodyPart> bodyPartDict = new();
    private readonly Dictionary<BodyPartType, Quaternion> fixedLocalTargets = new();
    private readonly Dictionary<BodyPartType, DriveProfile> driveProfileDict = new();

    private float logTimer = 0f;

    private void Awake()
    {
        InitBodyPartDic();
        InitDriveProfileDic();
        CaptureInitialLocalPose();
        InitDrive();
    }

    private void FixedUpdate()
    {
        ApplyLocalPose();
        UpdateDebugLog();
    }

    private void InitBodyPartDic()
    {
        bodyPartDict.Clear();

        foreach (BodyPart part in bodyParts)
        {
            if (part == null)
                continue;

            if (part.Type == BodyPartType.Default)
                continue;

            if (bodyPartDict.ContainsKey(part.Type))
                continue;

            bodyPartDict.Add(part.Type, part);
        }
    }

    private void InitDriveProfileDic()
    {
        driveProfileDict.Clear();

        foreach (DriveProfile profile in driveProfiles)
        {
            if (profile == null)
                continue;

            if (profile.type == BodyPartType.Default)
                continue;

            if (driveProfileDict.ContainsKey(profile.type))
                continue;

            driveProfileDict.Add(profile.type, profile);
        }
    }

    private void CaptureInitialLocalPose()
    {
        fixedLocalTargets.Clear();

        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;

            if (part.AnimatedBody == null)
                continue;

            fixedLocalTargets[pair.Key] = part.AnimatedBody.localRotation;
        }
    }

    private void InitDrive()
    {
        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;
            ConfigurableJoint joint = part.Joint;

            if (joint == null)
                continue;

            joint.rotationDriveMode = RotationDriveMode.Slerp;

            ApplyDriveProfile(joint, pair.Key);
        }
    }

    private void ApplyDriveProfile(ConfigurableJoint joint, BodyPartType type)
    {
        float spring = defaultSpring;
        float damper = defaultDamper;
        float maxForce = defaultMaxForce;

        if (driveProfileDict.TryGetValue(type, out DriveProfile profile))
        {
            spring = profile.spring;
            damper = profile.damper;
            maxForce = profile.maxForce;
        }

        JointDrive drive = joint.slerpDrive;
        drive.positionSpring = spring;
        drive.positionDamper = damper;
        drive.maximumForce = maxForce;
        joint.slerpDrive = drive;
    }

    private void ApplyLocalPose()
    {
        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;
            ConfigurableJoint joint = part.Joint;

            if (joint == null)
                continue;

            if (!fixedLocalTargets.TryGetValue(pair.Key, out Quaternion targetLocal))
                continue;

            Quaternion currentLocal = part.transform.localRotation;
            Quaternion delta = targetLocal * Quaternion.Inverse(currentLocal);

            joint.targetRotation = delta;
        }
    }

    private void UpdateDebugLog()
    {
        logTimer += Time.fixedDeltaTime;
        if (logTimer < logInterval)
            return;

        logTimer = 0f;

        List<string> logs = new();

        foreach (KeyValuePair<BodyPartType, BodyPart> pair in bodyPartDict)
        {
            BodyPart part = pair.Value;

            if (!fixedLocalTargets.TryGetValue(pair.Key, out Quaternion targetLocal))
                continue;

            float error = Quaternion.Angle(part.transform.localRotation, targetLocal);

            if (error < logErrorThreshold)
                continue;

            logs.Add($"{pair.Key}: {error:F2}");
        }

        if (logs.Count > 0)
        {
            Debug.Log(string.Join(" | ", logs));
        }
    }
}
using UnityEngine;

public class JointRotationFix : MonoBehaviour
{
    [SerializeField] private ConfigurableJoint joint;

    [ContextMenu("Fix Initial Rotation")]
    private void FixInitialRotation()
    {
        if (joint == null || joint.connectedBody == null)
        {
            Debug.LogError("joint 또는 connectedBody 없음");
            return;
        }

        joint.configuredInWorldSpace = false;

        Quaternion worldToJointSpace = Quaternion.LookRotation(
            joint.axis,
            joint.secondaryAxis
        );

        Quaternion connectedRotation = joint.connectedBody.rotation;
        Quaternion selfRotation = transform.rotation;

        Quaternion relative = Quaternion.Inverse(connectedRotation) * selfRotation;

        Quaternion target = Quaternion.Inverse(relative);

        joint.targetRotation = target;

        Debug.Log($"[{name}] targetRotation set");
    }
}
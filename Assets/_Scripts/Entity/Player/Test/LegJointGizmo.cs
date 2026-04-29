using UnityEngine;

public class LegJointGizmo : MonoBehaviour
{
    [SerializeField] private ConfigurableJoint leftUpperLegJoint;
    [SerializeField] private ConfigurableJoint rightUpperLegJoint;
    [SerializeField] private float sphereRadius = 0.03f;

    private void OnDrawGizmos()
    {
        DrawJoint(leftUpperLegJoint, Color.red, Color.green, Color.yellow);
        DrawJoint(rightUpperLegJoint, Color.cyan, Color.magenta, Color.white);
    }

    [ContextMenu("Log Joint Diagnostics")]
    private void LogJointDiagnostics()
    {
        Debug.Log("===== Joint Diagnostics Begin =====", this);

        LogJoint(leftUpperLegJoint, "LeftUpperLeg");
        LogJoint(rightUpperLegJoint, "RightUpperLeg");

        if (leftUpperLegJoint != null && rightUpperLegJoint != null)
        {
            Vector3 leftAnchorWorld = leftUpperLegJoint.transform.TransformPoint(leftUpperLegJoint.anchor);
            Vector3 leftConnectedWorld = leftUpperLegJoint.connectedBody != null
                ? leftUpperLegJoint.connectedBody.transform.TransformPoint(leftUpperLegJoint.connectedAnchor)
                : Vector3.zero;

            Vector3 rightAnchorWorld = rightUpperLegJoint.transform.TransformPoint(rightUpperLegJoint.anchor);
            Vector3 rightConnectedWorld = rightUpperLegJoint.connectedBody != null
                ? rightUpperLegJoint.connectedBody.transform.TransformPoint(rightUpperLegJoint.connectedAnchor)
                : Vector3.zero;

            Debug.Log(
                "[Compare]\n" +
                $"Left Anchor World: {leftAnchorWorld}\n" +
                $"Left Connected World: {leftConnectedWorld}\n" +
                $"Right Anchor World: {rightAnchorWorld}\n" +
                $"Right Connected World: {rightConnectedWorld}\n" +
                $"Left Anchor Y - Right Anchor Y: {leftAnchorWorld.y - rightAnchorWorld.y}\n" +
                $"Left Connected Y - Right Connected Y: {leftConnectedWorld.y - rightConnectedWorld.y}\n" +
                $"Left/Right Anchor Distance: {Vector3.Distance(leftAnchorWorld, rightAnchorWorld)}\n" +
                $"Left/Right Connected Distance: {Vector3.Distance(leftConnectedWorld, rightConnectedWorld)}",
                this);
        }

        Debug.Log("===== Joint Diagnostics End =====", this);
    }

    private void DrawJoint(ConfigurableJoint joint, Color anchorColor, Color connectedColor, Color lineColor)
    {
        if (joint == null || joint.connectedBody == null)
            return;

        Vector3 worldAnchor = joint.transform.TransformPoint(joint.anchor);
        Vector3 worldConnectedAnchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);

        Gizmos.color = anchorColor;
        Gizmos.DrawSphere(worldAnchor, sphereRadius);

        Gizmos.color = connectedColor;
        Gizmos.DrawSphere(worldConnectedAnchor, sphereRadius);

        Gizmos.color = lineColor;
        Gizmos.DrawLine(worldAnchor, worldConnectedAnchor);
    }

    private void LogJoint(ConfigurableJoint joint, string label)
    {
        if (joint == null)
        {
            Debug.LogWarning($"[{label}] Joint is null", this);
            return;
        }

        Rigidbody connectedBody = joint.connectedBody;
        if (connectedBody == null)
        {
            Debug.LogWarning($"[{label}] ConnectedBody is null", joint);
            return;
        }

        Transform jointTransform = joint.transform;
        Transform connectedTransform = connectedBody.transform;

        Vector3 worldAnchor = jointTransform.TransformPoint(joint.anchor);
        Vector3 worldConnectedAnchor = connectedTransform.TransformPoint(joint.connectedAnchor);
        float anchorDistance = Vector3.Distance(worldAnchor, worldConnectedAnchor);

        Quaternion worldDelta = Quaternion.Inverse(connectedTransform.rotation) * jointTransform.rotation;
        Vector3 worldDeltaEuler = worldDelta.eulerAngles;

        Quaternion localDelta = Quaternion.Inverse(connectedTransform.localRotation) * jointTransform.localRotation;
        Vector3 localDeltaEuler = localDelta.eulerAngles;

        Debug.Log(
            $"[{label}]\n" +
            $"Joint Object: {jointTransform.name}\n" +
            $"Connected Body: {connectedBody.name}\n\n" +

            $"Anchor Local: {joint.anchor}\n" +
            $"Connected Anchor Local: {joint.connectedAnchor}\n" +
            $"Anchor World: {worldAnchor}\n" +
            $"Connected Anchor World: {worldConnectedAnchor}\n" +
            $"Anchor Distance: {anchorDistance}\n\n" +

            $"Joint Position World: {jointTransform.position}\n" +
            $"Joint Position Local: {jointTransform.localPosition}\n" +
            $"Connected Position World: {connectedTransform.position}\n" +
            $"Connected Position Local: {connectedTransform.localPosition}\n\n" +

            $"Joint Rotation World Euler: {jointTransform.rotation.eulerAngles}\n" +
            $"Joint Rotation Local Euler: {jointTransform.localRotation.eulerAngles}\n" +
            $"Connected Rotation World Euler: {connectedTransform.rotation.eulerAngles}\n" +
            $"Connected Rotation Local Euler: {connectedTransform.localRotation.eulerAngles}\n" +
            $"World Rotation Delta Euler: {worldDeltaEuler}\n" +
            $"Local Rotation Delta Euler: {localDeltaEuler}\n\n" +

            $"Axis: {joint.axis}\n" +
            $"Secondary Axis: {joint.secondaryAxis}\n\n" +

            $"X Motion: {joint.xMotion}\n" +
            $"Y Motion: {joint.yMotion}\n" +
            $"Z Motion: {joint.zMotion}\n" +
            $"Angular X Motion: {joint.angularXMotion}\n" +
            $"Angular Y Motion: {joint.angularYMotion}\n" +
            $"Angular Z Motion: {joint.angularZMotion}\n\n" +

            $"Auto Configure Connected Anchor: {joint.autoConfigureConnectedAnchor}\n" +
            $"Enable Collision: {joint.enableCollision}\n" +
            $"Enable Preprocessing: {joint.enablePreprocessing}\n" +
            $"Configured In World Space: {joint.configuredInWorldSpace}\n" +
            $"Mass Scale: {joint.massScale}\n" +
            $"Connected Mass Scale: {joint.connectedMassScale}",
            joint);
    }
}
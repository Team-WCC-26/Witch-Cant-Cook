using System.Collections.Generic;
using UnityEngine;

public class JointConnectionGizmo : MonoBehaviour
{
    [SerializeField] private List<ConfigurableJoint> connectedJoints = new();
    [SerializeField] private float sphereRadius = 0.03f;

    private void OnDrawGizmos()
    {
        if (connectedJoints == null)
            return;

        for (int i = 0; i < connectedJoints.Count; i++)
        {
            ConfigurableJoint joint = connectedJoints[i];

            if (joint == null || joint.connectedBody == null)
                continue;

            Vector3 worldAnchor = joint.transform.TransformPoint(joint.anchor);
            Vector3 worldConnectedAnchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(worldAnchor, sphereRadius);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(worldConnectedAnchor, sphereRadius);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(worldAnchor, worldConnectedAnchor);
        }
    }
}
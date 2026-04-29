using UnityEngine;

public class HeadGizmo : MonoBehaviour
{
    [SerializeField] private ConfigurableJoint joint;

    private void OnDrawGizmos()
    {
        if (joint == null || joint.connectedBody == null)
            return;

        Vector3 worldAnchor = transform.TransformPoint(joint.anchor);
        Vector3 worldConnectedAnchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(worldAnchor, 0.03f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(worldConnectedAnchor, 0.03f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(worldAnchor, worldConnectedAnchor);
    }
}
using UnityEngine;

[ExecuteAlways]
public class JointAxisGizmo : MonoBehaviour
{
    [SerializeField] private ConfigurableJoint joint;
    [SerializeField] private float length = 0.3f;

    private void OnDrawGizmos()
    {
        if (joint == null)
            return;

        Transform t = joint.transform;

        // joint 기준 로컬 축 → 월드로 변환
        Vector3 axis = t.TransformDirection(joint.axis);
        Vector3 secondary = t.TransformDirection(joint.secondaryAxis);
        Vector3 third = Vector3.Cross(axis, secondary).normalized;

        Vector3 pos = t.position;

        // axis (빨강)
        Gizmos.color = Color.red;
        Gizmos.DrawLine(pos, pos + axis * length);

        // secondary (초록)
        Gizmos.color = Color.green;
        Gizmos.DrawLine(pos, pos + secondary * length);

        // third (파랑)
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(pos, pos + third * length);
    }
}
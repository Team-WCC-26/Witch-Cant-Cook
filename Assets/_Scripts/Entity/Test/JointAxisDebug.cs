using UnityEngine;

public class JointAxisDebug : MonoBehaviour
{
    [SerializeField] private ConfigurableJoint joint;

    private void Reset()
    {
        joint = GetComponent<ConfigurableJoint>();
    }

    [ContextMenu("Debug Joint Axis")]
    private void DebugJointAxis()
    {
        if (joint == null)
        {
            Debug.LogError("joint가 비어 있음");
            return;
        }

        if (joint.connectedBody == null)
        {
            Debug.LogError("connectedBody가 비어 있음");
            return;
        }

        Transform self = joint.transform;
        Transform connected = joint.connectedBody.transform;

        Vector3 connectedRightInSelfLocal = self.InverseTransformDirection(connected.right).normalized;
        Vector3 connectedUpInSelfLocal = self.InverseTransformDirection(connected.up).normalized;
        Vector3 connectedForwardInSelfLocal = self.InverseTransformDirection(connected.forward).normalized;

        Debug.Log($"[{self.name}] connected = {connected.name}");
        Debug.Log($"connected.right   -> self local = {connectedRightInSelfLocal}");
        Debug.Log($"connected.up      -> self local = {connectedUpInSelfLocal}");
        Debug.Log($"connected.forward -> self local = {connectedForwardInSelfLocal}");

        Vector3 suggestedAxis = connectedRightInSelfLocal;
        Vector3 suggestedSecondaryAxis = connectedUpInSelfLocal;

        Debug.Log($"Suggested axis          = {suggestedAxis}");
        Debug.Log($"Suggested secondaryAxis = {suggestedSecondaryAxis}");

        Vector3 cross = Vector3.Cross(suggestedAxis, suggestedSecondaryAxis).normalized;
        Debug.Log($"Cross(axis, secondary)  = {cross}");
        Debug.Log($"Dot(axis, secondary)    = {Vector3.Dot(suggestedAxis, suggestedSecondaryAxis)}");
    }
}
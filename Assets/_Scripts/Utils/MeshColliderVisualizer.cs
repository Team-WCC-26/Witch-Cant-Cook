using UnityEngine;

[RequireComponent(typeof(MeshCollider))]
public class MeshColliderVisualizer : MonoBehaviour
{
    private void OnDrawGizmosSelected()
    {
        if (!enabled) return;

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider.sharedMesh == null) return;

        Gizmos.color = Color.green;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireMesh(meshCollider.sharedMesh);
    }
}

using UnityEngine;

// Compatibility for older saved scenes. New boards do not create page controls.
[DisallowMultipleComponent]
public sealed class StagePageCylinder : MonoBehaviour
{
    private void Awake() => gameObject.SetActive(false);
}

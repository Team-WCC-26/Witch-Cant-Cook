using UnityEngine;

public sealed class FaceCamera : MonoBehaviour
{
    private Transform camTransform;

    private void OnEnable()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null) camTransform = mainCam.transform;
    }

    private void LateUpdate()
    {
        if (camTransform == null) return;

        transform.rotation = camTransform.rotation;
    }
}
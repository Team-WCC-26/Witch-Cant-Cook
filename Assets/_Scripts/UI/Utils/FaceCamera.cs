using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(10000)]
public sealed class FaceCamera : MonoBehaviour
{
    private Transform ingredientAnchor;
    private Vector3 initialLocalPosition;

    private void Awake()
    {
        Transform parent = transform.parent;
        if (parent != null && parent.TryGetComponent(out IngredientReaction _))
        {
            ingredientAnchor = parent;
            initialLocalPosition = transform.localPosition;
        }
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    private void LateUpdate()
    {
        Face(Camera.main);
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera == Camera.main)
            Face(camera);
    }

    private void Face(Camera camera)
    {
        if (camera == null) return;

        // Keep ingredient gauges above their pivot even when the ingredient rolls.
        if (ingredientAnchor != null)
        {
            float height = Mathf.Abs(initialLocalPosition.y * ingredientAnchor.lossyScale.y);
            transform.position = ingredientAnchor.position + Vector3.up * height;
        }

        Vector3 directionFromCamera = transform.position - camera.transform.position;
        if (directionFromCamera.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(directionFromCamera, camera.transform.up);
    }
}

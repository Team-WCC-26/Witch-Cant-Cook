using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class RoomOcclusionCulling : MonoBehaviour
{
    [SerializeField, Min(0f)] private float transitionDistance = 1f;
    [SerializeField] private Door lobbyDoor;
    [SerializeField] private Door kitchenDoor;
    [SerializeField] private float kitchenBoundaryOffset;
    [SerializeField] private Renderer[] lobbyRenderers = new Renderer[0];
    [SerializeField] private Renderer[] kitchenRenderers = new Renderer[0];

    private Renderer[] renderers;
    private bool[] initialStates;
    private readonly Stack<bool[]> cameraStates = new();
    private readonly Stack<bool[]> availableStates = new();

    public int HiddenRendererCount { get; private set; }

    #region Unity Callbacks

    private void OnEnable()
    {
        renderers = new Renderer[lobbyRenderers.Length + kitchenRenderers.Length];
        lobbyRenderers.CopyTo(renderers, 0);
        kitchenRenderers.CopyTo(renderers, lobbyRenderers.Length);
        initialStates = new bool[renderers.Length];
        availableStates.Clear();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        while (cameraStates.Count > 0) RestoreCamera();
    }

    private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        bool[] previousStates = availableStates.Count > 0
            ? availableStates.Pop() : new bool[renderers.Length];
        bool isFirstCamera = cameraStates.Count == 0;
        float distance = Vector3.Dot(camera.transform.position - transform.position, transform.forward);
        bool hideLobby = distance < -transitionDistance && IsClosed(lobbyDoor);
        bool hideKitchen = distance > kitchenBoundaryOffset + transitionDistance && IsClosed(kitchenDoor);
        HiddenRendererCount = 0;

        for (int index = 0; index < renderers.Length; index++)
        {
            Renderer target = renderers[index];
            if (target == null) continue;
            previousStates[index] = target.forceRenderingOff;
            if (isFirstCamera) initialStates[index] = previousStates[index];
            bool isLobby = index < lobbyRenderers.Length;
            bool hide = isLobby ? hideLobby : hideKitchen;
            target.forceRenderingOff = initialStates[index] || hide;
            if (hide && target.enabled && target.gameObject.activeInHierarchy) HiddenRendererCount++;
        }

        cameraStates.Push(previousStates);
    }

    private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (cameraStates.Count > 0) RestoreCamera();
    }

    #endregion

    private bool IsClosed(Door door)
    {
        return door != null && door.gameObject.activeInHierarchy && !door.IsOpen;
    }

    private void RestoreCamera()
    {
        bool[] previousStates = cameraStates.Pop();
        for (int index = 0; index < renderers.Length; index++)
        {
            if (renderers[index] != null)
                renderers[index].forceRenderingOff = previousStates[index];
        }
        availableStates.Push(previousStates);
    }

    #region Editor
#if UNITY_EDITOR
    [ContextMenu("Rebuild Static Renderer Groups")]
    private void RebuildStaticRendererGroups()
    {
        bool wasEnabled = enabled;
        if (wasEnabled) enabled = false;
        var lobby = new List<Renderer>();
        var kitchen = new List<Renderer>();
        Vector3 normal = transform.forward;
        Vector3 absoluteNormal = new(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        if (lobbyDoor == null || kitchenDoor == null)
        {
            if (wasEnabled) enabled = true;
            return;
        }

        Bounds lobbyBounds = GetDoorBounds(lobbyDoor);
        Bounds kitchenBounds = GetDoorBounds(kitchenDoor);
        transform.position = lobbyBounds.center;
        kitchenBoundaryOffset = Vector3.Dot(kitchenBounds.center - transform.position, normal);

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            foreach (Renderer target in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!target.gameObject.isStatic || target is ParticleSystemRenderer) continue;
                Bounds bounds = target.bounds;
                float distance = Vector3.Dot(bounds.center - transform.position, normal);
                float radius = Vector3.Dot(bounds.extents, absoluteNormal);
                // Shared walls and floors must remain visible from both rooms.
                if (distance > radius) lobby.Add(target);
                else if (distance + radius < kitchenBoundaryOffset) kitchen.Add(target);
            }
        }

        UnityEditor.Undo.RecordObject(this, "Rebuild Room Occlusion Groups");
        lobbyRenderers = lobby.ToArray();
        kitchenRenderers = kitchen.ToArray();
        UnityEditor.EditorUtility.SetDirty(this);
        if (wasEnabled) enabled = true;
    }

    private Bounds GetDoorBounds(Door door)
    {
        Renderer[] doorRenderers = door.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = doorRenderers.Length > 0
            ? doorRenderers[0].bounds : new Bounds(door.transform.position, Vector3.zero);
        foreach (Renderer target in doorRenderers) bounds.Encapsulate(target.bounds);
        return bounds;
    }
#endif
    #endregion
}

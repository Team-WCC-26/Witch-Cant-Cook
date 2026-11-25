using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FeatureRevealShot : MonoBehaviour
{
    [Serializable]
    internal sealed class PreviewSpawn
    {
        [SerializeField, Tooltip("Assign a visual-only prefab for the presentation.")]
        private GameObject prefab;
        [SerializeField] private Transform spawnPoint;

        public GameObject Prefab => prefab;
        public Transform SpawnPoint => spawnPoint;
        public bool IsConfigured => prefab != null && spawnPoint != null;

        public PreviewSpawn(GameObject prefab, Transform spawnPoint)
        {
            this.prefab = prefab;
            this.spawnPoint = spawnPoint;
        }
    }

    [SerializeField, Tooltip("Camera marker supplying position and rotation. Uses this object's Transform when left empty.")]
    private Transform cameraPosition;
    [SerializeField, Min(0f)] private float moveSeconds = 0.5f;
    [SerializeField, Tooltip("Optional scene oven whose door opens after the camera arrives.")]
    private OvenDoorController ovenDoor;
    [SerializeField] private PreviewSpawn[] previewSpawns = Array.Empty<PreviewSpawn>();
    // The original field names preserve references on Shots saved before the list was added.
    [SerializeField, HideInInspector] private GameObject previewPrefab;
    [SerializeField, HideInInspector] private Transform spawnPoint;
    [SerializeField, Min(0f), Tooltip("Seconds to wait before revealing the preview props after the camera arrives.")]
    private float preRevealDelay;
    [SerializeField, Min(0f)] private float revealSeconds = 0.4f;
    [SerializeField, Min(0f)] private float holdSeconds = 0.8f;
    [SerializeField] private Sprite popupImage;

    public Transform CameraPosition => cameraPosition != null ? cameraPosition : transform;
    public float MoveSeconds => moveSeconds;
    internal OvenDoorController OvenDoor => ovenDoor;
    internal float PreRevealDelay => preRevealDelay;
    public float RevealSeconds => revealSeconds;
    public float HoldSeconds => holdSeconds;
    public Sprite PopupImage => popupImage;

    internal IReadOnlyList<PreviewSpawn> GetPreviewSpawns()
    {
        MigratePreviewSpawn();
        return previewSpawns ?? Array.Empty<PreviewSpawn>();
    }

    internal bool IsConfigured
    {
        get
        {
            foreach (PreviewSpawn spawn in GetPreviewSpawns())
                if (spawn == null || !spawn.IsConfigured) return false;
            return true;
        }
    }

    private void OnValidate() => MigratePreviewSpawn();

    private void MigratePreviewSpawn()
    {
        if (previewPrefab == null) return;
        if (previewSpawns == null || previewSpawns.Length == 0)
            previewSpawns = new[] { new PreviewSpawn(previewPrefab, spawnPoint) };
        previewPrefab = null;
        spawnPoint = null;
    }
}

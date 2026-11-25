using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class FeatureRevealPresentation : MonoBehaviour
{
    [Serializable]
    private sealed class Presentation
    {
        [Min(1)] public int clearedStage = 1;
        public FeatureRevealShot[] shots;
        public FeatureRevealPopup popupPrefab;
    }

    [SerializeField] private Camera presentationCamera;
    [SerializeField] private Canvas popupCanvas;
    [SerializeField] private Presentation[] presentations;
    [SerializeField, Min(1)] private int previewClearedStage = 1;

    private static FeatureRevealPresentation activePresentation;
    public static bool IsPlaying => activePresentation != null;

    private Coroutine playback;
    private Camera previousCamera;
    private bool previousCameraEnabled;
    private Camera previousCanvasCamera;
    private FeatureRevealPopup popup;
    private GameObject previewRoot;
    private OvenDoorController previewOvenDoor;
    private bool waitingForShotConfirmation;
    private int shotNumber;
    private bool shotConfirmedLocally;
    private Action<int> requestShotConfirmation;
    private Action confirmed;
    private bool reboundCanvas;

    private void Awake()
    {
        if (presentationCamera != null)
            presentationCamera.enabled = false;
    }

    internal bool HasPresentation(int clearedStage) =>
        presentations != null && Array.Exists(presentations, entry => entry?.clearedStage == clearedStage);

    public bool TryPlay(int clearedStage, Action onConfirmed = null) =>
        TryPlay(clearedStage, onConfirmed, null);

    internal bool TryPlay(int clearedStage, Action onConfirmed, Action<int> confirmShot)
    {
        if (!isActiveAndEnabled || IsPlaying || StageReturnFlow.IsSettlementActive ||
            presentationCamera == null || !presentationCamera.gameObject.activeInHierarchy ||
            popupCanvas == null || !popupCanvas.isActiveAndEnabled || presentations == null)
            return false;

        Presentation entry = Array.Find(presentations, candidate => candidate?.clearedStage == clearedStage);
        if (entry == null || !CanPlay(entry)) return false;

        Camera source = ResolveGameplayCamera();
        if (source == null || !source.isActiveAndEnabled || source == presentationCamera) return false;

        activePresentation = this;
        confirmed = onConfirmed;
        requestShotConfirmation = confirmShot;
        StageReturnFlow.StopPlayerActions();
        previousCamera = source;
        previousCameraEnabled = source.enabled;
        presentationCamera.targetDisplay = source.targetDisplay;
        presentationCamera.targetTexture = source.targetTexture;
        presentationCamera.rect = source.rect;
        presentationCamera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        if (popupCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            previousCanvasCamera = popupCanvas.worldCamera;
            popupCanvas.worldCamera = presentationCamera;
            reboundCanvas = true;
        }
        source.enabled = false;
        presentationCamera.enabled = true;
        playback = StartCoroutine(Play(entry));
        return true;
    }

    [ContextMenu("Preview Feature Reveal (Play Mode)")]
    private void Preview()
    {
        if (Application.isPlaying && !TryPlay(previewClearedStage))
            Debug.LogWarning("Feature reveal preview requires an active gameplay camera, a popup Canvas, and valid scene shots.", this);
    }

    private static bool CanPlay(Presentation entry)
    {
        if (entry.popupPrefab == null || !entry.popupPrefab.IsConfigured ||
            entry.shots == null || entry.shots.Length == 0) return false;

        foreach (FeatureRevealShot shot in entry.shots)
            if (shot == null || !shot.IsConfigured) return false;
        return true;
    }

    private IEnumerator Play(Presentation entry)
    {
        yield return null;
        shotNumber = 0;
        foreach (FeatureRevealShot shot in entry.shots)
        {
            shotNumber++;
            OvenDoorController ovenDoor = shot.OvenDoor;
            if (ovenDoor != null)
            {
                if (ovenDoor.TryPrepareOpeningPreview()) previewOvenDoor = ovenDoor;
                else Debug.LogWarning("The oven door is unavailable for the feature reveal shot.", ovenDoor);
            }

            yield return MoveCamera(shot.CameraPosition, shot.MoveSeconds);
            if (previewOvenDoor != null)
                yield return previewOvenDoor.PlayOpeningPreview();

            IReadOnlyList<FeatureRevealShot.PreviewSpawn> spawns = shot.GetPreviewSpawns();
            if (spawns.Count > 0)
            {
                if (shot.PreRevealDelay > 0f)
                    yield return new WaitForSecondsRealtime(shot.PreRevealDelay);
                yield return SpawnPreviews(spawns, shot.RevealSeconds);
            }
            if (shot.HoldSeconds > 0f)
                yield return new WaitForSecondsRealtime(shot.HoldSeconds);

            if (popup == null)
            {
                popup = Instantiate(entry.popupPrefab, popupCanvas.transform, false);
                popup.Bind(ConfirmShot);
            }
            waitingForShotConfirmation = true;
            shotConfirmedLocally = false;
            popup.transform.SetAsLastSibling();
            popup.Show(shot.PopupImage);
            yield return new WaitUntil(() => !waitingForShotConfirmation);
            RemovePreview();
        }

        Action onConfirmed = confirmed;
        playback = null;
        Cancel();
        onConfirmed?.Invoke();
    }

    private IEnumerator SpawnPreviews(IReadOnlyList<FeatureRevealShot.PreviewSpawn> spawns, float seconds)
    {
        // Prepare all props before enabling them together.
        previewRoot = new GameObject("Feature Reveal Preview");
        previewRoot.SetActive(false);
        var visuals = new (Transform Transform, Vector3 FullScale)[spawns.Count];
        for (int i = 0; i < spawns.Count; i++)
        {
            FeatureRevealShot.PreviewSpawn spawn = spawns[i];
            Transform marker = spawn.SpawnPoint;
            GameObject preview = Instantiate(spawn.Prefab, marker.position, marker.rotation, previewRoot.transform);
            foreach (Collider collider in preview.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in preview.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.detectCollisions = false;
            }

            Transform visual = preview.transform;
            visuals[i] = (visual, visual.localScale);
            if (seconds > 0f) visual.localScale = Vector3.zero;
            preview.SetActive(true);
        }

        previewRoot.SetActive(true);
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            float remaining = elapsed / seconds - 1f;
            // Overshoot once, then settle at the prefab's authored scale.
            float scale = 1f + remaining * remaining * (2.7f * remaining + 1.7f);
            SetPreviewScale(visuals, Mathf.Max(0f, scale));
            yield return null;
        }
        SetPreviewScale(visuals, 1f);
    }

    private static void SetPreviewScale((Transform Transform, Vector3 FullScale)[] visuals, float scale)
    {
        foreach (var visual in visuals)
            if (visual.Transform != null) visual.Transform.localScale = visual.FullScale * scale;
    }

    private IEnumerator MoveCamera(Transform destination, float seconds)
    {
        Transform cameraTransform = presentationCamera.transform;
        Vector3 startPosition = cameraTransform.position;
        Quaternion startRotation = cameraTransform.rotation;
        Vector3 targetPosition = destination.position;
        Quaternion targetRotation = destination.rotation;
        for (float elapsed = 0f; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
            cameraTransform.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, progress),
                Quaternion.Slerp(startRotation, targetRotation, progress));
            yield return null;
        }
        cameraTransform.SetPositionAndRotation(targetPosition, targetRotation);
    }

    private void RemovePreview()
    {
        if (previewOvenDoor != null) previewOvenDoor.EndOpeningPreview();
        previewOvenDoor = null;
        if (previewRoot == null) return;
        previewRoot.SetActive(false);
        Destroy(previewRoot);
        previewRoot = null;
    }

    private void ConfirmShot()
    {
        if (!waitingForShotConfirmation || shotConfirmedLocally) return;
        if (requestShotConfirmation != null)
        {
            shotConfirmedLocally = true;
            popup.SetWaiting(true);
            requestShotConfirmation(shotNumber);
            return;
        }
        AdvanceShot(shotNumber);
    }

    internal void AdvanceShot(int number)
    {
        if (!waitingForShotConfirmation || number != shotNumber ||
            (requestShotConfirmation != null && !shotConfirmedLocally)) return;
        waitingForShotConfirmation = false;
        popup.gameObject.SetActive(false);
    }

    internal void RetryShot(int number)
    {
        if (!waitingForShotConfirmation || number != shotNumber) return;
        shotConfirmedLocally = false;
        popup.SetWaiting(false);
    }

    public void Cancel()
    {
        if (activePresentation != this) return;
        if (playback != null) StopCoroutine(playback);
        playback = null;
        waitingForShotConfirmation = false;
        RemovePreview();

        if (popup != null)
        {
            popup.gameObject.SetActive(false);
            Destroy(popup.gameObject);
        }
        popup = null;
        if (reboundCanvas && popupCanvas != null) popupCanvas.worldCamera = previousCanvasCamera;
        reboundCanvas = false;
        previousCanvasCamera = null;
        if (presentationCamera != null) presentationCamera.enabled = false;
        if (previousCamera != null) previousCamera.enabled = previousCameraEnabled;
        previousCamera = null;
        confirmed = null;
        requestShotConfirmation = null;
        activePresentation = null;
    }

    private void OnDisable() => Cancel();

    internal static void CancelActive()
    {
        if (activePresentation != null) activePresentation.Cancel();
    }

    private static Camera ResolveGameplayCamera()
    {
        PlayerSpawnManager players = PlayerSpawnManager.Instance;
        if (players != null && players.TryGetPlayer(players.MyID, out PlayerBrain player) &&
            player != null && player.PlayerCam != null)
            return player.PlayerCam;
        return Camera.main;
    }
}

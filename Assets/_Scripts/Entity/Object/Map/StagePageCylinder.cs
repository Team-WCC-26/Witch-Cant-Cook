using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StagePageCylinder : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    [SerializeField, Tooltip("Optional mesh or pivot to swing while the hit collider stays still.")]
    private Transform visualRoot;
    [SerializeField, Min(0f)] private float swingAngle = 22f;
    [SerializeField, Min(0f)] private float swingDuration = 0.65f;

    private StageSelectionController board;
    private int direction;
    private Coroutine swing;
    private Transform swingingTransform;
    private Quaternion restRotation;

    public EntityCategory Category => EntityCategory.None;
    public EntityCategory AcceptedCategories => EntityCategory.Player;

    internal void Bind(StageSelectionController controller, int pageDirection)
    {
        board = controller;
        direction = pageDirection;
    }

    public void OnPunched(PlayerBrain player)
    {
        if (!isActiveAndEnabled || board == null || player == null) return;
        if (swingAngle > 0f && swingDuration > 0f)
            PlaySwing(player);
        board.RequestPageChange(player, direction);
    }

    private void PlaySwing(PlayerBrain player)
    {
        StopSwing();
        swingingTransform = visualRoot != null ? visualRoot : transform;
        restRotation = swingingTransform.localRotation;

        Vector3 away = Vector3.ProjectOnPlane(swingingTransform.position - player.transform.position, Vector3.up);
        if (away.sqrMagnitude < 0.0001f)
            away = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
        Vector3 axis = swingingTransform.InverseTransformDirection(Vector3.Cross(Vector3.up, away.normalized));
        swing = StartCoroutine(Swing(axis));
    }

    private IEnumerator Swing(Vector3 axis)
    {
        for (float elapsed = 0f; elapsed < swingDuration && swingingTransform != null; elapsed += Time.deltaTime)
        {
            float progress = elapsed / swingDuration;
            float decay = 1f - progress;
            float tilt = Mathf.Sin(progress * Mathf.PI * 6f) * decay * decay * swingAngle;
            swingingTransform.localRotation = restRotation * Quaternion.AngleAxis(tilt, axis);
            yield return null;
        }
        swing = null;
        StopSwing();
    }

    private void StopSwing()
    {
        if (swing != null) StopCoroutine(swing);
        swing = null;
        if (swingingTransform != null) swingingTransform.localRotation = restRotation;
        swingingTransform = null;
    }

    private void OnDisable() => StopSwing();
}

using Protocol;
using Server;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LeaveRoomTriggerInteraction : MapObjInteraction, IPunchReceiver
{
    private const string IdlePrompt = "나가려면 펀치";
    private const string ActivePrompt = "한 번 더!";
    private const float BounceDuration = 0.48f;

    [SerializeField] private Transform magicBall;
    private TMP_Text prompt;
    private double activeUntil;
    private float bounceElapsed = BounceDuration;
    private Vector3 ballRestPosition;
    private Vector3 ballRestScale;

    public override EntityCategory AcceptedCategories => IsRegistered ? EntityCategory.Player : EntityCategory.None;

    private void Awake()
    {
        if (magicBall != null)
        {
            ballRestPosition = magicBall.localPosition;
            ballRestScale = magicBall.localScale;
        }
        CreatePrompt();
    }

    public void ApplyState(LeaveRoomTriggerState state, long remainingMs)
    {
        bool active = state == LeaveRoomTriggerState.Active && remainingMs > 0;
        activeUntil = active ? Time.unscaledTimeAsDouble + remainingMs / 1000d : 0d;
        if (prompt != null) prompt.text = active ? ActivePrompt : IdlePrompt;
    }

    public void OnPunched(PlayerBrain player)
    {
        if (!isActiveAndEnabled || !IsRegistered || player == null ||
            PlayerSpawnManager.Instance?.IsMine(player.PlayerId) != true ||
            ServerManager.Instance?.IsConnected != true || RoomTitleReturn.IsRunning) return;

        if (magicBall != null)
        {
            ResetBounce();
            bounceElapsed = 0f;
        }
        player.Interact.RequestEntityInteract(NetworkId);
    }

    private void LateUpdate()
    {
        if (activeUntil > 0d && Time.unscaledTimeAsDouble >= activeUntil)
            ApplyState(LeaveRoomTriggerState.Idle, 0);

        if (magicBall == null || bounceElapsed >= BounceDuration) return;
        bounceElapsed += Time.unscaledDeltaTime;
        if (bounceElapsed >= BounceDuration)
        {
            ResetBounce();
            return;
        }

        float progress = bounceElapsed / BounceDuration;
        float spring = Mathf.Sin(progress * Mathf.PI * 3f) * (1f - progress);
        magicBall.localPosition = ballRestPosition + Vector3.up * (0.16f * Mathf.Max(0f, spring));
        magicBall.localScale = Vector3.Scale(ballRestScale,
            new Vector3(1f - 0.10f * spring, 1f + 0.18f * spring, 1f - 0.10f * spring));
    }

    private void OnDisable()
    {
        if (magicBall != null && bounceElapsed < BounceDuration) ResetBounce();
    }

    private void ResetBounce()
    {
        bounceElapsed = BounceDuration;
        magicBall.localPosition = ballRestPosition;
        magicBall.localScale = ballRestScale;
    }

    private void CreatePrompt()
    {
        var canvasObject = new GameObject("ExitPrompt", typeof(RectTransform), typeof(Canvas), typeof(Image));
        canvasObject.layer = LayerMask.NameToLayer("UI");
        var canvasRect = (RectTransform)canvasObject.transform;
        canvasRect.SetParent(transform, false);
        canvasRect.localPosition = Vector3.up * 1.75f;
        canvasRect.localScale = Vector3.one * 0.0025f;
        canvasRect.sizeDelta = new Vector2(560f, 124f);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        canvasObject.AddComponent<FaceCamera>();

        Image background = canvasObject.GetComponent<Image>();
        background.color = new Color(0.12f, 0.07f, 0.04f, 0.9f);
        background.raycastTarget = false;

        var textObject = new GameObject("PromptText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.layer = canvasObject.layer;
        var textRect = (RectTransform)textObject.transform;
        textRect.SetParent(canvasRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 8f);
        textRect.offsetMax = new Vector2(-20f, -8f);

        prompt = textObject.GetComponent<TextMeshProUGUI>();
        prompt.font = TMP_Settings.defaultFontAsset;
        prompt.text = activeUntil > Time.unscaledTimeAsDouble ? ActivePrompt : IdlePrompt;
        prompt.fontSize = 68f;
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.color = Color.white;
        prompt.raycastTarget = false;
    }
}

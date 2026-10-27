using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class StagePageCylinder : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    public enum PageDirection { Previous = -1, Next = 1 }
    [SerializeField] private StageSelectionController board;
    [SerializeField] private PageDirection direction = PageDirection.Next;
    [SerializeField, Min(0f)] private float wobbleAngle = 12f;
    [SerializeField, Min(0.01f)] private float wobbleDuration = 0.35f;

    private MeshFilter sourceMesh;
    private MeshRenderer sourceRenderer;
    private MeshFilter wobbleMesh;
    private MeshRenderer wobbleRenderer;
    private Transform wobblePivot;
    private MaterialPropertyBlock propertyBlock;
    private float wobbleElapsed;
    private float wobbleDirection = 1f;
    private bool wobbling;

    private void Awake()
    {
        sourceMesh = GetComponent<MeshFilter>();
        sourceRenderer = GetComponent<MeshRenderer>();
    }

    public EntityCategory Category => EntityCategory.None;
    public PageDirection Direction => direction;
    public EntityCategory AcceptedCategories => EntityCategory.Player;

    public void Configure(StageSelectionController controller, PageDirection pageDirection)
    {
        board = controller;
        direction = pageDirection;
    }

    private void OnCollisionEnter(Collision collision) => ReceiveContact(collision.collider);
    private void OnTriggerEnter(Collider other) => ReceiveContact(other);

    private void ReceiveContact(Collider other)
    {
        PlayerBrain player = other.GetComponentInParent<PlayerBrain>();
        if (player != null && board != null) board.TurnPage((int)direction, player);
    }

    public void OnPunched(PlayerBrain player)
    {
        PlayClickWobble(player);
        if (board != null) board.TurnPage((int)direction, player);
    }

    private void PlayClickWobble(PlayerBrain player)
    {
        if (player == null || wobbleAngle <= 0f || sourceMesh == null ||
            sourceRenderer == null || sourceMesh.sharedMesh == null ||
            (!wobbling && !sourceRenderer.enabled)) return;

        if (wobblePivot == null) CreateWobbleVisual();

        wobbleMesh.sharedMesh = sourceMesh.sharedMesh;
        wobbleRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
        for (int i = 0; i < wobbleRenderer.sharedMaterials.Length; i++)
        {
            sourceRenderer.GetPropertyBlock(propertyBlock, i);
            wobbleRenderer.SetPropertyBlock(propertyBlock, i);
        }

        Bounds bounds = sourceMesh.sharedMesh.bounds;
        Vector3 pivot = new(bounds.center.x, bounds.min.y, bounds.center.z);
        wobblePivot.localPosition = pivot;
        wobblePivot.GetChild(0).localPosition = -pivot;
        wobblePivot.localRotation = Quaternion.identity;
        wobblePivot.gameObject.SetActive(true);
        sourceRenderer.enabled = false;

        Vector3 toPlayer = player.transform.position - transform.position;
        float side = Vector3.Dot(transform.right, toPlayer.normalized);
        wobbleDirection = Mathf.Abs(side) > 0.15f ? Mathf.Sign(side) : -wobbleDirection;
        wobbleElapsed = 0f;
        wobbling = true;
    }

    private void CreateWobbleVisual()
    {
        GameObject pivotObject = new("StageClickWobblePivot");
        wobblePivot = pivotObject.transform;
        wobblePivot.SetParent(transform, false);

        GameObject visualObject = new("StageClickWobbleVisual");
        visualObject.layer = gameObject.layer;
        visualObject.transform.SetParent(wobblePivot, false);
        wobbleMesh = visualObject.AddComponent<MeshFilter>();
        wobbleRenderer = visualObject.AddComponent<MeshRenderer>();
        wobbleRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
        wobbleRenderer.receiveShadows = sourceRenderer.receiveShadows;
        wobbleRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
        wobbleRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
        wobbleRenderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
        wobbleRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        wobbleRenderer.sortingOrder = sourceRenderer.sortingOrder;
        propertyBlock = new MaterialPropertyBlock();
    }

    private void LateUpdate()
    {
        if (!wobbling) return;

        wobbleElapsed += Time.deltaTime;
        float progress = Mathf.Clamp01(wobbleElapsed / wobbleDuration);
        float tilt = Mathf.Sin(progress * Mathf.PI * 3f) * (1f - progress) * wobbleAngle * wobbleDirection;
        wobblePivot.localRotation = Quaternion.Euler(tilt * 0.25f, 0f, tilt);
        if (progress >= 1f) StopWobble();
    }

    private void OnDisable() => StopWobble();

    private void StopWobble()
    {
        if (!wobbling) return;
        wobbling = false;
        if (wobblePivot != null)
        {
            wobblePivot.localRotation = Quaternion.identity;
            wobblePivot.gameObject.SetActive(false);
        }
        if (sourceRenderer != null) sourceRenderer.enabled = true;
    }
}

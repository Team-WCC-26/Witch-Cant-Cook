using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class StagePageCylinder : MonoBehaviour, IInteractTarget, IPunchReceiver
{
    public enum PageDirection { Previous = -1, Next = 1 }
    [SerializeField] private StageSelectionController board;
    [SerializeField] private PageDirection direction = PageDirection.Next;

    public EntityCategory Category => EntityCategory.None;
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
        if (player != null) OnPunched(player);
    }

    public void OnPunched(PlayerBrain player)
    {
        if (board != null) board.TurnPage((int)direction, player);
    }
}

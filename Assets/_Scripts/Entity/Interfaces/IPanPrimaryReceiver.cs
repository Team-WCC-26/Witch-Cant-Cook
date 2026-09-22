public interface IPanPrimaryReceiver
{
    // 팬 좌클릭 상호작용을 대상이 처리한다.
    bool TryReceivePanPrimary(PanInteraction pan, PlayerInteract player);
}

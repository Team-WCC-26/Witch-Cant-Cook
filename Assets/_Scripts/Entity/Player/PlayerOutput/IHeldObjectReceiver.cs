public interface IHeldObjectReceiver
{
    bool TryReceiveHeldObject(CatchableObj heldObj, PlayerInteract interact);
}

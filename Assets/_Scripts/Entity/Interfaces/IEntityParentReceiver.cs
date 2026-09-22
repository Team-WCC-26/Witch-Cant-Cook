public interface IEntityParentReceiver
{
    void HandleEntityAdded(CatchableObj entity);
    void HandleEntityRemoved(CatchableObj entity);
}

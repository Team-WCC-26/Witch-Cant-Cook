public interface IHeldSecondaryAction
{
    // true consumes the input, including attempts to repeat an active special action.
    bool TryUseSecondary(PlayerInteract interact);
}

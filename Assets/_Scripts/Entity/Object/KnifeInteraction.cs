using UnityEngine;

public class KnifeInteraction : MonoBehaviour, IHeldPrimaryAction, IEquipment
{
    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        if (!interact.TryUseEquipment()) return true;

        CatchableObj target = interact.FindInteractTarget<CatchableObj>();
        if (target == null) return true;
        if (!target.TryGetComponent(out IngredientReaction _)) return true;

        interact.RequestEntityInteract(target.NetworkId);
        return true;
    }
}
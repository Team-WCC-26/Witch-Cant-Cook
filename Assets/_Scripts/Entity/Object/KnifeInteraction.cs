using UnityEngine;

public class KnifeInteraction : MonoBehaviour, IHeldPrimaryAction, IEquipment
{
    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        if (!interact.TryUseEquipment()) return true;

        interact.TryCutTarget();
        return true;
    }
}

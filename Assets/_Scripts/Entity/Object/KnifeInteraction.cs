using UnityEngine;

public class KnifeInteraction : MonoBehaviour, IHeldPrimaryAction
{
    public bool TryUsePrimary(PlayerInteract interact)
    {
        if (interact == null) return false;

        interact.TryCutTarget();
        return true;
    }
}

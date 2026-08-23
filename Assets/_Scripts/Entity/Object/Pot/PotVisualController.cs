using UnityEngine;

public class PotVisualController : MonoBehaviour
{
    [SerializeField] private PotItemContainer soupContainer;
    [SerializeField] private PotItemContainer stewContainer;

    private CatchableObj primary;
    private CatchableObj secondary;

    private void Awake()
    {
        DisableLegacyMesh(soupContainer);
        DisableLegacyMesh(stewContainer);
        HideAll();
    }

    public void ShowPrimary(CatchableObj entity)
    {
        // Slot A
        if (entity == null) return;

        primary = entity;
        secondary = null;
        PlaceAt(primary.transform, soupContainer.transform);
    }

    public void ShowCombined(CatchableObj result, CatchableObj combinedVisual)
    {
        // Slot B
        ShowPrimary(result);
        if (combinedVisual == null) return;

        secondary = combinedVisual;
        secondary.transform.SetParent(primary.transform, true);
        secondary.transform.SetPositionAndRotation(
            stewContainer.transform.position,
            stewContainer.transform.rotation);
    }

    public void ApplyCookedVisual()
    {
        ApplyBoiled(primary);
        ApplyBoiled(secondary);
    }

    public void HideAll()
    {
        primary = null;
        secondary = null;
    }

    private static void PlaceAt(Transform target, Transform slot)
    {
        target.SetParent(slot, false);
        target.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
    }

    private static void ApplyBoiled(CatchableObj entity)
    {
        if (entity != null && entity.TryGetComponent(out IngredientReaction reaction))
            reaction.ApplyServerAction(IngredientAction.Boil);
    }

    private static void DisableLegacyMesh(PotItemContainer container)
    {
        if (container == null) return;
        if (container.PotMeshRenderer != null)
            container.PotMeshRenderer.enabled = false;
    }
}

using UnityEngine;

public sealed class CostumeMirrorTarget : MonoBehaviour, IInteractTarget
{
    public EntityCategory Category => EntityCategory.CostumeMirror;
    public EntityCategory AcceptedCategories => EntityCategory.All;
}

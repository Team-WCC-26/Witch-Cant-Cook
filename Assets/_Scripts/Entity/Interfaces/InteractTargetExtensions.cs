using UnityEngine;

public static class InteractTargetExtensions
{
    // 상대 기준: 상대가 나를 허용하는가
    // 상대 <- 나
    public static bool Accepts(
        this IInteractTarget target,
        EntityCategory sourceCategory)
    {
        if (target == null) return false;
        if (sourceCategory == EntityCategory.None) return false;

        return (target.AcceptedCategories & sourceCategory) != 0;
    }

    // 나 기준: 내가 상대와 상호작용할 수 있는가
    // 나 -> 상대
    public static bool CanInteractWith(
        this IInteractTarget source,
        IInteractTarget target)
    {
        if (source == null || target == null) return false;

        return target.Accepts(source.Category);
    }

    // 대상과 같은 오브젝트에 있는 컴포넌트를 찾기
    public static T GetTargetComponent<T>(
        this IInteractTarget target) where T : class
    {
        if (target is T directTarget)
            return directTarget;

        if (target is not Component component)
            return null;

        foreach (MonoBehaviour behaviour in component.GetComponents<MonoBehaviour>())
        {
            if (behaviour is T targetComponent)
                return targetComponent;
        }

        return null;
    }
}

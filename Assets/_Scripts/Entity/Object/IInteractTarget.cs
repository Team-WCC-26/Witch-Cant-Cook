using System;

[Flags]
public enum InteractCategory
{
    None = 0,
    EmptyHand = 1 << 0,
    Ingredient = 1 << 1,
    Pan = 1 << 2,
    Knife = 1 << 3,
    Plate = 1 << 4,
    Broom = 1 << 5,
    Bucket = 1 << 6,
    Stove = 1 << 7,
    PrepTable = 1 << 8,
    Oven = 1 << 9,
    Pot = 1 << 10,

    All = ~0
}

public interface IInteractTarget
{
    InteractCategory Category { get; } // 해당 오브젝트의 종류
    InteractCategory AcceptedCategories { get; } //이 오브젝트가 허용하는 상대 종류
}

public static class InteractTargetExtensions
{
    // 상대 기준: 상대가 나를 허용하는가
    // 상대 <- 나
    public static bool Accepts(
        this IInteractTarget target,
        InteractCategory sourceCategory)
    {
        if (target == null) return false;
        if (sourceCategory == InteractCategory.None) return false;

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
}

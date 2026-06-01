using Protocol;

namespace Server;

public class IngredientStatePair : IComparable<IngredientStatePair>, IEquatable<IngredientStatePair>
{
    public readonly int IngredientId;
    public readonly IngredientState ProcessState;

    public IngredientStatePair(int ingredientId, IngredientState processState)
    {
        IngredientId = ingredientId;
        ProcessState = processState;
    }

    public int CompareTo(IngredientStatePair? other)
    {
        int cmp = IngredientId.CompareTo(other.IngredientId);

        if (cmp != 0) return cmp;

        return ProcessState.CompareTo(other.ProcessState);
    }

    public bool Equals(IngredientStatePair? other)
    {
        return IngredientId == other.IngredientId && ProcessState == other.ProcessState;
    }

    public override bool Equals(object? obj)
    {
        return obj is IngredientStatePair other && Equals(other);
    }
}

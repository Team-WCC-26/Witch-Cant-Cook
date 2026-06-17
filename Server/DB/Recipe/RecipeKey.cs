using System.Diagnostics.CodeAnalysis;

namespace Server;

public readonly struct RecipeKey : IEquatable<RecipeKey>
{
    public readonly IngredientStatePair[] Ingredients;

    public RecipeKey(IEnumerable<IngredientStatePair> ingredients)
    {
        Ingredients = ingredients.Order().ToArray();
    }

    public RecipeKey(Ingredient a, Ingredient b)
        : this([new(a.IngredientId, a.ProcessState), new(b.IngredientId, b.ProcessState)]) { }

    public bool Equals(RecipeKey other)
    {
        if (Ingredients.Length != other.Ingredients.Length) return false;

        for (int i = 0; i < Ingredients.Length; i++)
        {
            if (!Ingredients[i].Equals(other.Ingredients[i])) return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        return obj is RecipeKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        foreach (var ingredient in Ingredients)
        {
            hash.Add(ingredient);
        }

        return hash.ToHashCode();
    }

    public bool IsSubSetOf(RecipeKey other)
    {
        int i = 0;
        int j = 0;

        while (i < Ingredients.Length && j < other.Ingredients.Length)
        {
            int cmp = Ingredients[i].CompareTo(other.Ingredients[j]);

            if (cmp == 0)
            {
                i++;
                j++;
            }
            else if (cmp > 0)
            {
                j++;
            }
            else
            {
                return false;
            }
        }

        return i == Ingredients.Length;
    }
}

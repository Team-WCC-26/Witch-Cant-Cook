namespace Server;

public class Dish(int toolId) : ContainerTool(toolId)
{
    public override bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        if (other is Dish) return false;

        if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

        Ingredient = other as Ingredient;

        return true;
    }
}

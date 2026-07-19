namespace Server;

public class Dish(int toolId) : ContainerTool(toolId)
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient? Ingredient => Entity as Ingredient;

    public override bool TryCombine(ICombinable other, out ICombinable combinable)
    {
        combinable = this;

        if (other is Dish) return false;

        if (Ingredient != null && !Ingredient.TryCombine(other, out other)) return false;

        Entity = other as Entity;

        return true;
    }
}

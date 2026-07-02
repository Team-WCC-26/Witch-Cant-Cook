namespace Server;

public abstract class ContainerTool(int toolId) : Tool(toolId), ICombinable
{
    public int IngredientId => (Ingredient != null) ? Ingredient.IngredientId : -1;
    public Ingredient? Ingredient { get; protected set; }

    public abstract bool TryCombine(ICombinable other, out ICombinable combinable);

    public void Clear()
    {
        Ingredient = null;
    }
}
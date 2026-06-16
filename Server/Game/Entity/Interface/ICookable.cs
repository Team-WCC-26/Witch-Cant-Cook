using Protocol;

namespace Server;

public interface ICookable
{
    bool TryCook(IngredientState state, out Ingredient ingredient);
}

using Protocol;

namespace Server;

public interface ICombinable
{
    bool TryCombine(ICombinable other, out ICombinable combinable);
    IngredientStateData[] GetIngredients();
}

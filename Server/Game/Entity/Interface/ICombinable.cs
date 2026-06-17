using Protocol;

namespace Server;

public interface ICombinable
{
    int IngredientId { get; }
    bool TryCombine(ICombinable other, out ICombinable combinable);
}

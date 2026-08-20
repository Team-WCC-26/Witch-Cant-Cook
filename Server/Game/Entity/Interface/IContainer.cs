namespace Server;

public interface IContainer
{
    Ingredient? Ingredient { get; }

    bool TryInsert(Ingredient ingredient);
    Ingredient? Take();
}

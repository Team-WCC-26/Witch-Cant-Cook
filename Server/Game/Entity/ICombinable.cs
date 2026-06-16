namespace Server;

public interface ICombinable
{
    bool TryCombine(ICombinable other, out Food food);
}

public interface IInteractTarget
{
    EntityCategory Category { get; } // 해당 오브젝트의 종류
    EntityCategory AcceptedCategories { get; } //이 오브젝트가 허용하는 상대 종류
}
using System;
using System.Collections.Generic;

public sealed class DishOrder
{
    public long Id { get; }
    public int RecipeId { get; }
    public double ReceivedAt { get; }
    public double DurationSeconds { get; private set; }
    public void SetDurationIfUnknown(double seconds)
    {
        if (DurationSeconds <= 0 && seconds > 0) DurationSeconds = seconds;
    }
    public DishOrder(long id, int recipeId, double receivedAt, double durationSeconds)
    {
        Id = id; RecipeId = recipeId; ReceivedAt = receivedAt; DurationSeconds = durationSeconds;
    }
    public double Remaining(double now) => DurationSeconds > 0 ? Math.Max(0, DurationSeconds - (now - ReceivedAt)) : double.PositiveInfinity;
    public bool IsExpired(double now) => DurationSeconds > 0 && Remaining(now) <= 0;
}

// Expired orders remain until the server acknowledges them, so a late Fail cannot remove a newer duplicate.
public sealed class DishOrderBook
{
    private readonly List<DishOrder> orders = new();
    private long nextId;
    public int Count => orders.Count;
    public DishOrder this[int index] => orders[index];
    public void Add(int recipeId, double receivedAt, double durationSeconds) =>
        orders.Add(new DishOrder(++nextId, recipeId, receivedAt, durationSeconds));
    public bool Complete(int recipeId)
    {
        int index = orders.FindIndex(order => order.RecipeId == recipeId);
        if (index < 0) return false;
        orders.RemoveAt(index);
        return true;
    }
    public void GetVisible(double now, int capacity, List<DishOrder> result)
    {
        result.Clear();
        foreach (DishOrder order in orders)
        {
            if (result.Count >= capacity) break;
            if (!order.IsExpired(now)) result.Add(order);
        }
    }
    public void Clear() => orders.Clear();
}

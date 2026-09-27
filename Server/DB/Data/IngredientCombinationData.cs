using Newtonsoft.Json;
using Protocol;

namespace Server;

public class IngredientCombinationData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("recipeID")]
    public int ResultId { get; init; }

    [JsonProperty("ingID")]
    public int IngredientId { get; init; }

    [JsonProperty("amount")]
    public int Amount { get; init; }

    [JsonProperty("conditionFlag")]
    public IngredientState ConditionFlag { get; init; }
}

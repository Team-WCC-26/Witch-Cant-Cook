using Newtonsoft.Json;
using Protocol;

namespace Server;

public class IngredientCombinationData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("ingID")]
    public int ResultId { get; init; }

    [JsonProperty("comID1")]
    public int IngredientId { get; init; }

    [JsonProperty("conditionFlag")]
    public IngredientState ConditionFlag { get; init; }
}

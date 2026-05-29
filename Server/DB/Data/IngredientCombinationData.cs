using Newtonsoft.Json;
using Protocol;

namespace Server;

public class IngredientCombinationData
{
    [JsonProperty("id")]
    public int Id;

    [JsonProperty("ingID")]
    public int ResultId;

    [JsonProperty("comID1")]
    public int IngredientId;

    [JsonProperty("conditionFlag")]
    public IngredientState ConditionFlag;
}

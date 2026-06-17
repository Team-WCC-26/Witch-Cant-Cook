using Newtonsoft.Json;
using Protocol;

namespace Server;

public class DishData
{
    [JsonProperty("id")]
    public int Id;

    [JsonProperty("name")]
    public string Name;

    [JsonProperty("prefabName")]
    public string PrefabName;

    [JsonProperty("ingredientID")]
    public string IngredientId;

    [JsonProperty("conditionFlag")]
    public IngredientState ConditionFlag;
}

using Newtonsoft.Json;
using Protocol;

namespace Server;

public class IngredientData
{
    [JsonProperty("id")]
    public int Id;

    [JsonProperty("name")]
    public string Name;

    [JsonProperty("prefabName")]
    public string PrefabName;

    [JsonProperty("statID")]
    public string StatId;

    [JsonProperty("conditionFlag")]
    public IngredientState ConditionFlag; 
}

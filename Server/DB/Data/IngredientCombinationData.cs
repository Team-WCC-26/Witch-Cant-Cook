using Newtonsoft.Json;

namespace Server;

public class IngredientCombinationData
{
    [JsonProperty("id")]
    public int Id;

    [JsonProperty("ingID")]
    public int IngId;

    [JsonProperty("comID1")]
    public int ComId;

    [JsonProperty("conditionFlag")]
    public byte ConditionFlag;
}

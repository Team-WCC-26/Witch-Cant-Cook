using Newtonsoft.Json;

namespace Server;

public class DishData
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
    public byte ConditionFlag;
}

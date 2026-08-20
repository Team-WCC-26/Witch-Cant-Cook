using Newtonsoft.Json;
using Protocol;

namespace Server;

public class IngredientData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("prefabName")]
    public string PrefabName { get; init; }

    [JsonProperty("statID")]
    public int StatId { get; init; }

    [JsonProperty("conditionFlag")]
    public IngredientState InvalidProcessFlag { get; init; }

    [JsonProperty("spawnProbability")]
    public int SpawnWeight { get; init; }
}

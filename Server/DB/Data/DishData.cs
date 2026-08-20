using Newtonsoft.Json;
using Protocol;

namespace Server;

public class DishData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("prefabName")]
    public string PrefabName { get; init; }

    [JsonProperty("ingredientID")]
    public int IngredientId { get; init; }

    [JsonProperty("finalConditionFlag")]
    public IngredientState ConditionFlag { get; init; }

    [JsonProperty("timeLimit")]
    public int TimeLimit { get; init; }

    [JsonProperty("nextRecipeSpawnDelay")]
    public int SpawnDelay { get; init; }

    [JsonProperty("spawnProbability")]
    public int SpawnWeight { get; init; }
}

using Newtonsoft.Json;

namespace Server;

public class IngredientStatData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("hp")]
    public int Hp { get; init; }

    [JsonProperty("weight")]
    public float Weight { get; init; }

    [JsonProperty("damage")]
    public int Damage { get; init; }

    [JsonProperty("isAttachCountertop")]
    public bool CanAttachToCounterTop { get; init; }
}

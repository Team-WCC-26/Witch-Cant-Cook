using Newtonsoft.Json;

namespace Server;

public class ToolData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("prefabName")]
    public string PrefabName { get; init; }

    [JsonProperty("damage")]
    public int Damage { get; init; }
}

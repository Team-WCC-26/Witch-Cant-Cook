using Newtonsoft.Json;

namespace Server;

public class RecipeGroup
{
    [JsonProperty("grooviroom")]
    public int GroupId { get; init; }

    [JsonProperty("difficulty")]
    public int Difficulty { get; init; }

    [JsonProperty("ingredient1")]
    public int RecipeId { get; init; }
}
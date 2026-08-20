using Newtonsoft.Json;

namespace Server;

public class IngredientGroup
{
    [JsonProperty("grooviroom")]
    public int GroupId { get; init; }

    [JsonProperty("difficulty")]
    public int Difficulty { get; init; }

    [JsonProperty("ingredient1")]
    public int IngredientId { get; init; }

    [JsonProperty("beltID")]
    public int BeltId { get; init; }
}

using Newtonsoft.Json;

namespace Server;

public class StageData
{
    [JsonProperty("id")]
    public int Id { get; init; }

    [JsonProperty("stageNum")]
    public int StageNum { get; init; }

    [JsonProperty("goal")]
    public int Goal { get; init; }

    [JsonProperty("tip")]
    public string Tip { get; init; }
}

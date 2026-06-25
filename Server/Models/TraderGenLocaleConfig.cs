using System.Text.Json.Serialization;

namespace TraderGen.Models;

// TraderGen locale override config. Lets users choose the language for trader/quest text.
public class TraderGenLocaleConfig
{
    // Language key to use for TraderGen packs. Set to "auto" or leave empty to use SPT's locale.json.
    [JsonPropertyName("language")]
    public string Language { get; set; } = "auto";
}

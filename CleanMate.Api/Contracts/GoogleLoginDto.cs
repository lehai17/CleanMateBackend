using System.Text.Json.Serialization;

namespace CleanMate.Api.Contracts
{
    public class GoogleLoginDto
    {
        [JsonPropertyName("idToken")]
        public string IdToken { get; set; } = string.Empty;
    }
}

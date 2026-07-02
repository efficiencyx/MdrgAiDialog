using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MdrgAiDialog.JunApi;
using MdrgAiDialog.Utils;

namespace MdrgAiDialog.Tts;

/// <summary>
/// Client for speech synthesis servers. Two API formats are supported:
/// </summary>
/// <remarks>
/// - "Jun": the Jun webapp's /api/tts.php?action=tts endpoint (Kokoro/pocket-tts
///   behind the PHP proxy + NGINX). Reuses the shared authenticated JunSession,
///   so TTS traffic is logged and rate-limited like the web UI's.
/// - "OpenAI": POST {ApiUrl}/audio/speech with an OpenAI-style JSON body
///   (works with OpenAI TTS, Kokoro-FastAPI, openedai-speech, AllTalk, ...).
/// Both are expected to return WAV bytes
/// </remarks>
public class TtsClient {
  private static readonly Logger logger = new("TtsClient");

  private readonly TtsConfig config;
  private readonly bool useJunFormat;
  private readonly HttpClient openAiClient;

  public TtsClient(TtsConfig config) {
    this.config = config;
    useJunFormat = config.ApiFormat.Equals("Jun", StringComparison.OrdinalIgnoreCase);

    if (!useJunFormat) {
      openAiClient = new HttpClient {
        BaseAddress = new Uri(EnsureTrailingSlash(config.ApiUrl)),
        Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds)
      };

      if (!string.IsNullOrEmpty(config.ApiKey)) {
        openAiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config.ApiKey}");
      }
    }
  }

  /// <summary>
  /// Synthesizes a piece of text into decoded PCM audio
  /// </summary>
  /// <param name="text">Text to speak</param>
  /// <param name="cancellationToken">Token that aborts the request</param>
  /// <returns>Decoded audio, or null when synthesis failed (errors are logged, never thrown)</returns>
  public async Task<WavAudio> Synthesize(string text, CancellationToken cancellationToken) {
    try {
      var response = useJunFormat
        ? await SynthesizeJun(text, cancellationToken)
        : await SynthesizeOpenAi(text, cancellationToken);

      if (response == null) {
        return null;
      }

      using (response) {
        if (response.StatusCode == HttpStatusCode.NoContent) {
          // Nothing speakable in this chunk (the Jun TTS sidecar returns 204)
          return null;
        }

        if (!response.IsSuccessStatusCode) {
          var body = await response.Content.ReadAsStringAsync();
          logger.LogError($"TTS request failed: {response.StatusCode} - {Truncate(body, 300)}");
          return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync();
        return WavAudio.Parse(bytes);
      }
    } catch (OperationCanceledException) {
      // Session was stopped, not an error
      return null;
    } catch (Exception ex) {
      logger.LogError($"TTS synthesis failed: {ex.Message}");
      return null;
    }
  }

  private async Task<HttpResponseMessage> SynthesizeJun(string text, CancellationToken cancellationToken) {
    var session = JunSession.Instance;

    if (!await session.EnsureAuthenticated()) {
      logger.LogError("TTS skipped: not authenticated against the Jun webapp");
      return null;
    }

    // tts.php rejects texts over 2000 chars
    if (text.Length > 2000) {
      text = text[..2000];
    }

    var payload = new JunSpeechRequest {
      Text = text,
      Voice = config.Voice,
      Speed = Math.Clamp(config.Speed, 0.5, 2.0),
      Engine = config.Engine
    };

    var request = new HttpRequestMessage(HttpMethod.Post, "api/tts.php?action=tts") {
      Content = new StringContent(
        JsonSerializer.Serialize(payload, new JsonSerializerOptions {
          DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }),
        Encoding.UTF8,
        "application/json"
      )
    };

    var response = await session.Client.SendAsync(request, cancellationToken);

    if (response.StatusCode == HttpStatusCode.Unauthorized) {
      // Session expired; retry once with a fresh login
      session.InvalidateSession();
      if (await session.EnsureAuthenticated()) {
        response.Dispose();
        var retry = new HttpRequestMessage(HttpMethod.Post, "api/tts.php?action=tts") {
          Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        response = await session.Client.SendAsync(retry, cancellationToken);
      }
    }

    return response;
  }

  private async Task<HttpResponseMessage> SynthesizeOpenAi(string text, CancellationToken cancellationToken) {
    var payload = new OpenAiSpeechRequest {
      Model = config.Model,
      Input = text,
      Voice = config.Voice,
      ResponseFormat = "wav",
      Speed = config.Speed
    };

    var request = new HttpRequestMessage(HttpMethod.Post, "audio/speech") {
      Content = new StringContent(
        JsonSerializer.Serialize(payload, new JsonSerializerOptions {
          DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        }),
        Encoding.UTF8,
        "application/json"
      )
    };

    return await openAiClient.SendAsync(request, cancellationToken);
  }

  private static string EnsureTrailingSlash(string url) {
    return url.EndsWith("/") ? url : url + "/";
  }

  private static string Truncate(string value, int maxLength) {
    if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
      return value;
    }
    return value[..maxLength] + "...";
  }

  private class JunSpeechRequest {
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("voice")]
    public string Voice { get; set; }

    [JsonPropertyName("speed")]
    public double Speed { get; set; }

    [JsonPropertyName("engine")]
    public string Engine { get; set; }
  }

  private class OpenAiSpeechRequest {
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("input")]
    public string Input { get; set; }

    [JsonPropertyName("voice")]
    public string Voice { get; set; }

    [JsonPropertyName("response_format")]
    public string ResponseFormat { get; set; }

    [JsonPropertyName("speed")]
    public double Speed { get; set; }
  }
}

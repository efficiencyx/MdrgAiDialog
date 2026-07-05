using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MdrgAiDialog.JunApi;
using MdrgAiDialog.Utils;

namespace MdrgAiDialog.Ui;

/// <summary>
/// Fetches the list of models a provider actually exposes, so the picker can offer a real
/// choice instead of a blind text field. Every provider here speaks the OpenAI-compatible
/// <c>GET {ApiUrl}/models</c> shape except Jun, which has its own <c>/api/models.php</c>
/// (a thin proxy over Ollama's <c>/api/tags</c>). All failures degrade to <see cref="Curated"/>.
/// </summary>
public static class ModelCatalog {
  private static readonly Logger logger = new("ModelCatalog");

  // Returns the models a provider currently offers. Never throws; returns an empty list when
  // the provider can't be reached (the UI then falls back to Curated + a custom entry).
  public static async Task<List<string>> FetchAsync(string providerName, ModConfig.ProviderView view) {
    try {
      if (providerName == "Jun") {
        return await FetchJun();
      }
      if (view == null) {
        return [];
      }
      return await FetchOpenAiCompatible(providerName, view);
    } catch (Exception ex) {
      logger.LogWarning($"Model fetch for {providerName} failed: {ex.Message}");
      return [];
    }
  }

  private static async Task<List<string>> FetchJun() {
    var session = JunSession.Instance;
    if (!await session.EnsureAuthenticated()) {
      logger.LogWarning("Jun not authenticated; cannot list models");
      return [];
    }
    var response = await session.Client.GetAsync("api/models.php");
    if (!response.IsSuccessStatusCode) {
      return [];
    }
    var json = await response.Content.ReadAsStringAsync();
    return ParseModels(json);
  }

  private static async Task<List<string>> FetchOpenAiCompatible(string providerName, ModConfig.ProviderView view) {
    if (string.IsNullOrWhiteSpace(view.ApiUrl)) {
      return [];
    }

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    var url = view.ApiUrl.TrimEnd('/') + "/models";
    using var request = new HttpRequestMessage(HttpMethod.Get, url);

    if (!string.IsNullOrWhiteSpace(view.ApiKey)) {
      // Anthropic authenticates differently from the OpenAI-style Bearer header.
      if (providerName == "Claude") {
        request.Headers.TryAddWithoutValidation("x-api-key", view.ApiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
      } else {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {view.ApiKey}");
      }
    }

    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode) {
      logger.LogWarning($"{providerName} /models returned {(int)response.StatusCode}");
      return [];
    }
    var json = await response.Content.ReadAsStringAsync();
    return ParseModels(json);
  }

  // Extracts model ids from the shapes providers return: {"data":[{"id":..}]} (OpenAI),
  // {"models":["..",{"id"/"name":..}]} (Jun/Ollama), or a bare string array. Embedding
  // models are dropped (not usable for chat).
  private static List<string> ParseModels(string json) {
    var ids = new List<string>();
    try {
      using var doc = JsonDocument.Parse(json);
      var root = doc.RootElement;

      JsonElement array;
      if (root.ValueKind == JsonValueKind.Array) {
        array = root;
      } else if (root.ValueKind == JsonValueKind.Object &&
                 (root.TryGetProperty("data", out array) || root.TryGetProperty("models", out array))) {
        // array bound above
      } else {
        return ids;
      }

      if (array.ValueKind != JsonValueKind.Array) {
        return ids;
      }

      foreach (var item in array.EnumerateArray()) {
        string id = null;
        if (item.ValueKind == JsonValueKind.String) {
          id = item.GetString();
        } else if (item.ValueKind == JsonValueKind.Object) {
          if (item.TryGetProperty("id", out var idEl)) id = idEl.GetString();
          else if (item.TryGetProperty("name", out var nameEl)) id = nameEl.GetString();
        }
        if (string.IsNullOrWhiteSpace(id)) continue;
        if (id.Contains("embed", StringComparison.OrdinalIgnoreCase)) continue; // not a chat model
        if (!ids.Contains(id)) ids.Add(id);
      }
    } catch (Exception ex) {
      logger.LogWarning($"Could not parse model list: {ex.Message}");
    }
    ids.Sort(StringComparer.OrdinalIgnoreCase);
    return ids;
  }

  // Known-good defaults per provider, used when the live list can't be fetched.
  public static List<string> Curated(string providerName) => providerName switch {
    "Ollama" => ["hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF"],
    "OpenAI" => ["gpt-4.1-mini", "gpt-4.1", "gpt-4o-mini", "o4-mini"],
    "OpenRouter" => ["deepseek/deepseek-r1-0528:free", "meta-llama/llama-3.3-70b-instruct", "anthropic/claude-3.5-sonnet"],
    "Mistral" => ["mistral-small-2506", "mistral-large-latest", "open-mistral-nemo"],
    "Google" => ["gemini-3-flash", "gemini-3-pro", "gemini-2.5-flash"],
    "DeepSeek" => ["deepseek-chat", "deepseek-reasoner"],
    "Claude" => ["claude-haiku-4-5", "claude-sonnet-4-6", "claude-opus-4-8"],
    "Jun" => ["hf.co/efficiencyx/Jun-Lora-v2-GGUF:Q6_K", "hf.co/unsloth/gemma-4-12B-it-qat-GGUF:UD-Q4_K_XL"],
    _ => [],
  };
}

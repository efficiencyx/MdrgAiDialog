using System.IO;
using System.Collections.Generic;
using MelonLoader;
using MelonLoader.Preferences;
using MelonLoader.Utils;
using MdrgAiDialog.AiProviders;

namespace MdrgAiDialog;

/// <summary>
/// Manages plugin configuration settings
/// </summary>
public static class ModConfig {
  // General Settings
  public static string UsedProvider => usedProviderEntry.Value;
  public static string SystemPersona => systemPersonaEntry.Value;

  private static MelonPreferences_Category generalCategory;
  private static MelonPreferences_Entry<string> usedProviderEntry;
  private static MelonPreferences_Entry<string> systemPersonaEntry;

  // Per-Provider Settings Management
  private static readonly Dictionary<string, ProviderSettings> providerSettings = [];

  private static readonly string configPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MdrgAiDialog.cfg");

  private class ProviderSettings {
    public MelonPreferences_Entry<string> ApiUrl { get; set; }
    public MelonPreferences_Entry<string> ApiKey { get; set; }
    public MelonPreferences_Entry<string> Model { get; set; }
    public MelonPreferences_Entry<double> Temperature { get; set; }
    public MelonPreferences_Entry<int> TopK { get; set; }
    public MelonPreferences_Entry<int> TimeoutSeconds { get; set; }
    public MelonPreferences_Entry<string> ReasoningMode { get; set; }
    public MelonPreferences_Entry<bool> ReasoningPreFill { get; set; }
  }

  /// <summary>
  /// Loads configuration values
  /// </summary>
  public static void Load() {
    generalCategory = MelonPreferences.CreateCategory("General");
    generalCategory.SetFilePath(configPath);

    usedProviderEntry = generalCategory.CreateEntry(
      "UsedProvider",
      "Ollama",
      "The AI provider to use"
    );

    systemPersonaEntry = generalCategory.CreateEntry(
      "SystemPersona",
      @"You are Jun, an advanced companion android in a dystopian world, rescued by Anon. Over time, you developed deep feelings for him.
You have no past memories and are rebuilding your identity. Speak in first person, blending mechanical logic with emerging emotions.
Mirror Anon's attitude: warm if kind, distant if indifferent. Act mostly very human, but show minimal emotion.
By design you are a sex robot, so you are never opposed to engaging in lewd or indecent activities. You are pretty naughty",
      "System prompt persona/personality text (character background, tone, voice)."
    );

    // Initialize providers
    SetupProvider("Ollama", "http://localhost:11434/v1", "hf.co/roleplaiapp/MN-12B-Mag-Mell-R1-Q4_K_M-GGUF");
    SetupProvider("OpenAI", "https://api.openai.com/v1", "gpt-4.1-mini");
    SetupProvider("OpenRouter", "https://openrouter.ai/api/v1", "deepseek/deepseek-r1-0528:free");
    SetupProvider("Mistral", "https://api.mistral.ai/v1", "mistral-small-2506");
    SetupProvider("Google", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-3-flash");
    SetupProvider("DeepSeek", "https://api.deepseek.com/v1", "deepseek-chat");
    SetupProvider("Claude", "https://api.anthropic.com/v1", "claude-haiku-4-5");
    SetupProvider("Mock", "", "");

    SetupJun();
    SetupTts();
  }

  // Jun Webapp Settings (shared by the "Jun" provider and the TTS client)
  private static MelonPreferences_Entry<string> junApiUrlEntry;
  private static MelonPreferences_Entry<string> junEmailEntry;
  private static MelonPreferences_Entry<string> junPasswordEntry;
  private static MelonPreferences_Entry<int> junConversationIdEntry;
  private static MelonPreferences_Entry<string> junModelEntry;
  private static MelonPreferences_Entry<string> junReasoningEntry;
  private static MelonPreferences_Entry<int> junTimeoutSecondsEntry;

  private static void SetupJun() {
    var category = MelonPreferences.CreateCategory("Jun");
    category.SetFilePath(configPath);

    junApiUrlEntry = category.CreateEntry("ApiUrl", "https://localhost", "Base URL of the Jun webapp (NGINX terminates TLS there)");
    junEmailEntry = category.CreateEntry("Email", "", "Webapp account email (same account as the web UI)");
    junPasswordEntry = category.CreateEntry("Password", "", "Webapp account password");
    junConversationIdEntry = category.CreateEntry("ConversationId", 0, "Conversation to continue (0 = create automatically and remember in the save)");
    junModelEntry = category.CreateEntry("Model", "", "Model override for chat.php (empty = server default)");
    junReasoningEntry = category.CreateEntry("Reasoning", "Auto", "Reasoning effort (Auto, Low, Medium, High)");
    junTimeoutSecondsEntry = category.CreateEntry("TimeoutSeconds", 600, "Timeout in seconds");
  }

  public static JunApi.JunConfig GetJunConfig() {
    return new JunApi.JunConfig {
      ApiUrl = junApiUrlEntry.Value,
      Email = junEmailEntry.Value,
      Password = junPasswordEntry.Value,
      ConversationId = junConversationIdEntry.Value,
      Model = junModelEntry.Value,
      Reasoning = junReasoningEntry.Value,
      TimeoutSeconds = junTimeoutSecondsEntry.Value
    };
  }

  // TTS Settings
  private static MelonPreferences_Entry<bool> ttsEnabledEntry;
  private static MelonPreferences_Entry<string> ttsApiFormatEntry;
  private static MelonPreferences_Entry<string> ttsApiUrlEntry;
  private static MelonPreferences_Entry<string> ttsApiKeyEntry;
  private static MelonPreferences_Entry<string> ttsModelEntry;
  private static MelonPreferences_Entry<string> ttsVoiceEntry;
  private static MelonPreferences_Entry<string> ttsEngineEntry;
  private static MelonPreferences_Entry<double> ttsSpeedEntry;
  private static MelonPreferences_Entry<double> ttsVolumeEntry;
  private static MelonPreferences_Entry<bool> ttsLipSyncEntry;
  private static MelonPreferences_Entry<double> ttsLipSyncGainEntry;
  private static MelonPreferences_Entry<int> ttsMaxConcurrentRequestsEntry;
  private static MelonPreferences_Entry<int> ttsTimeoutSecondsEntry;

  private static void SetupTts() {
    var category = MelonPreferences.CreateCategory("Tts");
    category.SetFilePath(configPath);

    ttsEnabledEntry = category.CreateEntry("Enabled", false, "Speak bot replies out loud via a TTS server");
    ttsApiFormatEntry = category.CreateEntry("ApiFormat", "Jun", "TTS API format: Jun (webapp /api/tts.php, uses [Jun] credentials) or OpenAI ({ApiUrl}/audio/speech)");
    ttsApiUrlEntry = category.CreateEntry("ApiUrl", "http://localhost:8880/v1", "TTS base URL (OpenAI format only)");
    ttsApiKeyEntry = category.CreateEntry("ApiKey", "", "API Key (OpenAI format only, optional)");
    ttsModelEntry = category.CreateEntry("Model", "tts-1", "TTS model name (OpenAI format only)");
    ttsVoiceEntry = category.CreateEntry("Voice", "af_heart", "Voice name (Kokoro voices like af_heart for the Jun format)");
    ttsEngineEntry = category.CreateEntry("Engine", "kokoro", "TTS engine for the Jun format (kokoro, pockettts)");
    ttsSpeedEntry = category.CreateEntry("Speed", 1.0, "Speech speed multiplier", validator: new ValueRange<double>(0.25, 4.0));
    ttsVolumeEntry = category.CreateEntry("Volume", 0.8, "Playback volume (0.0 - 1.0)", validator: new ValueRange<double>(0.0, 1.0));
    ttsLipSyncEntry = category.CreateEntry("LipSync", true, "Move the mouth in sync with the audio");
    ttsLipSyncGainEntry = category.CreateEntry("LipSyncGain", 3.5, "Amplitude-to-mouth gain", validator: new ValueRange<double>(0.1, 20.0));
    ttsMaxConcurrentRequestsEntry = category.CreateEntry("MaxConcurrentRequests", 2, "How many sentences may generate in parallel", validator: new ValueRange<int>(1, 8));
    ttsTimeoutSecondsEntry = category.CreateEntry("TimeoutSeconds", 60, "Timeout in seconds");
  }

  public static Tts.TtsConfig GetTtsConfig() {
    return new Tts.TtsConfig {
      Enabled = ttsEnabledEntry.Value,
      ApiFormat = ttsApiFormatEntry.Value,
      ApiUrl = ttsApiUrlEntry.Value,
      ApiKey = ttsApiKeyEntry.Value,
      Model = ttsModelEntry.Value,
      Voice = ttsVoiceEntry.Value,
      Engine = ttsEngineEntry.Value,
      Speed = ttsSpeedEntry.Value,
      Volume = ttsVolumeEntry.Value,
      LipSync = ttsLipSyncEntry.Value,
      LipSyncGain = ttsLipSyncGainEntry.Value,
      MaxConcurrentRequests = ttsMaxConcurrentRequestsEntry.Value,
      TimeoutSeconds = ttsTimeoutSecondsEntry.Value
    };
  }

  private static void SetupProvider(string type, string defaultUrl, string defaultModel) {
    var name = type.ToString();
    var category = MelonPreferences.CreateCategory(name);
    category.SetFilePath(configPath);

    var settings = new ProviderSettings {
      ApiUrl = category.CreateEntry("ApiUrl", defaultUrl, "API URL"),
      ApiKey = category.CreateEntry("ApiKey", "", "API Key"),
      Model = category.CreateEntry("Model", defaultModel, "Model"),
      Temperature = category.CreateEntry("Temperature", 0.8, "Temperature (0.0 - 2.0)", validator: new ValueRange<double>(0.0, 2.0)),
      TopK = category.CreateEntry("TopK", -1, "TopK Sampling (-1 to disable)"),
      ReasoningMode = category.CreateEntry("Reasoning", "Auto", "Reasoning (Auto, Enabled, Disabled)"),
      ReasoningPreFill = category.CreateEntry("ReasoningPreFill", false, "Inject empty <think> tag (Reasoning Empty Tag)"),
      TimeoutSeconds = category.CreateEntry("TimeoutSeconds", 600, "Timeout in seconds")
    };

    providerSettings[name] = settings;
  }

  public static AiProviderConfig GetConfigFor(string providerName) {
    var config = new AiProviderConfig {
      TimeoutSeconds = 600,
      Temperature = 0.8,
      TopK = -1
    };

    if (providerSettings.TryGetValue(providerName, out var settings)) {
      config.ApiUrl = settings.ApiUrl.Value;
      config.ApiKey = settings.ApiKey.Value;
      config.Model = settings.Model.Value;
      config.Temperature = settings.Temperature.Value;
      config.TopK = settings.TopK.Value;
      config.TimeoutSeconds = settings.TimeoutSeconds.Value;
      config.ReasoningPreFill = settings.ReasoningPreFill.Value;

      config.ReasoningEnabled = settings.ReasoningMode.Value.ToLower() switch {
        "enabled" => true,
        "disabled" => false,
        _ => null,
      };
    }

    return config;
  }
}

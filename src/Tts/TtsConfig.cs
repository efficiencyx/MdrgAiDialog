namespace MdrgAiDialog.Tts;

/// <summary>
/// Configuration for the streaming TTS integration
/// </summary>
public class TtsConfig {
  public bool Enabled { get; set; }

  // "Jun" (the webapp's /api/tts.php behind NGINX+PHP, auth via the [Jun] block)
  // or "OpenAI" ({ApiUrl}/audio/speech with a Bearer key)
  public string ApiFormat { get; set; }

  // OpenAI format only; the Jun format uses the [Jun] ApiUrl
  public string ApiUrl { get; set; }
  public string ApiKey { get; set; }
  public string Model { get; set; }

  public string Voice { get; set; }

  // Jun format only: "kokoro" or "pockettts"
  public string Engine { get; set; }

  public double Speed { get; set; }

  // Sample-level gain (AudioSource.volume caps at 1.0, so quiet TTS is boosted here)
  public double Volume { get; set; }

  public bool LipSync { get; set; }
  public double LipSyncGain { get; set; }

  // How many TTS requests may generate in parallel while audio is playing
  public int MaxConcurrentRequests { get; set; }

  public int TimeoutSeconds { get; set; }
}

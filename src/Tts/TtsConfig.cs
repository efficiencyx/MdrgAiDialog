namespace MdrgAiDialog.Tts;

/// <summary>
/// Configuration for the streaming TTS integration
/// </summary>
public class TtsConfig {
  /// <summary>
  /// Whether TTS playback is enabled
  /// </summary>
  public bool Enabled { get; set; }

  /// <summary>
  /// API format: "Jun" (the Jun webapp's /api/tts.php behind NGINX+PHP,
  /// authenticated via the [Jun] config block) or "OpenAI"
  /// ({ApiUrl}/audio/speech with a Bearer key)
  /// </summary>
  public string ApiFormat { get; set; }

  /// <summary>
  /// Base URL of the TTS server. Only used with ApiFormat = "OpenAI";
  /// the Jun format uses the [Jun] ApiUrl
  /// </summary>
  public string ApiUrl { get; set; }

  /// <summary>
  /// API Key / token for the TTS server (OpenAI format only, optional)
  /// </summary>
  public string ApiKey { get; set; }

  /// <summary>
  /// Model name sent to the TTS server (OpenAI format only)
  /// </summary>
  public string Model { get; set; }

  /// <summary>
  /// Voice name sent to the TTS server (e.g. Kokoro's af_heart)
  /// </summary>
  public string Voice { get; set; }

  /// <summary>
  /// TTS engine for the Jun format: "kokoro" or "pockettts"
  /// </summary>
  public string Engine { get; set; }

  /// <summary>
  /// Speech speed multiplier (1.0 = normal)
  /// </summary>
  public double Speed { get; set; }

  /// <summary>
  /// Playback gain applied to the PCM (1.0 = original level, >1 amplifies quiet TTS,
  /// clamped to avoid overflow). AudioSource.volume caps at 1.0, hence a sample-level gain
  /// </summary>
  public double Volume { get; set; }

  /// <summary>
  /// Whether to drive the Live2D mouth parameter from the playing audio
  /// </summary>
  public bool LipSync { get; set; }

  /// <summary>
  /// Amplitude-to-mouth-openness gain used by lipsync
  /// </summary>
  public double LipSyncGain { get; set; }

  /// <summary>
  /// Maximum number of TTS requests generating in parallel while audio is playing
  /// </summary>
  public int MaxConcurrentRequests { get; set; }

  /// <summary>
  /// Request timeout in seconds
  /// </summary>
  public int TimeoutSeconds { get; set; }
}

namespace MdrgAiDialog.JunApi;

/// <summary>
/// Configuration for the Jun webapp stack (PHP proxy + NGINX in front of Ollama/Kokoro)
/// </summary>
public class JunConfig {
  /// <summary>
  /// Base URL of the webapp, e.g. https://your-host (NGINX terminates TLS there).
  /// Endpoints used: /api/auth.php, /api/chat.php, /api/conversations.php, /api/tts.php
  /// </summary>
  public string ApiUrl { get; set; }

  /// <summary>
  /// Webapp account email (same account as the web UI / Telegram bridge)
  /// </summary>
  public string Email { get; set; }

  /// <summary>
  /// Webapp account password
  /// </summary>
  public string Password { get; set; }

  /// <summary>
  /// Conversation id to continue. 0 = create one automatically on first chat
  /// and remember it in the save file
  /// </summary>
  public int ConversationId { get; set; }

  /// <summary>
  /// Model override sent to chat.php. Empty = let the server pick its default
  /// </summary>
  public string Model { get; set; }

  /// <summary>
  /// Reasoning effort forwarded to chat.php: Auto, Low, Medium or High
  /// </summary>
  public string Reasoning { get; set; }

  /// <summary>
  /// Request timeout in seconds
  /// </summary>
  public int TimeoutSeconds { get; set; }
}

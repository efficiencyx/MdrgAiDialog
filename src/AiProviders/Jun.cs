using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MdrgAiDialog.JunApi;
using MdrgAiDialog.Utils;

namespace MdrgAiDialog.AiProviders;

/// <summary>
/// AI provider for the Jun webapp stack (PHP proxy + NGINX in front of Ollama)
/// </summary>
/// <remarks>
/// Uses the same /api/chat.php endpoint the web UI does, so requests from the
/// game share the server's rate limits and logging and land in the same
/// server-side conversation store. chat.php persists both sides of every
/// exchange, so a chat started in the browser carries on in-game
/// and back (the history is pulled from /api/conversations.php at the start of
/// each chat).
///
/// The server injects its own system prompt (and strips any client-sent system
/// role), so the mod's #!-command instructions don't apply here. Instead the
/// [A:...] action tags the Jun finetune emits are translated to the game's
/// #!bot.* commands on the fly by <see cref="JunActionTranslator"/>
/// </remarks>
public class Jun : AiProvider {
  private static readonly Logger logger = new("Jun");

  private const string conversationSaveKey = "jun-conversation-id";
  private const int maxHistoryMessages = 78; // chat.php rejects > 80 messages per request

  private readonly JunSession session;
  private int conversationId = 0;

  public Jun(AiProviderConfig config) : base(config) {
    session = JunSession.Instance;
  }

  public override Task WarmUp() {
    // The server keeps the model loaded (it also serves the web UI)
    return Task.CompletedTask;
  }

  public override async Task<bool> EnsureReadyForChat() {
    if (!await session.EnsureAuthenticated()) {
      return false;
    }

    if (!await EnsureConversation()) {
      return false;
    }

    await PullServerHistory();
    return true;
  }

  public override async IAsyncEnumerable<string> SendMessage(string message) {
    messages.Add(new ChatMessage { Role = "user", Content = message });

    HttpResponseMessage response = null;
    string error = null;

    try {
      response = await MakeChatRequest();

      if (response.StatusCode == HttpStatusCode.Unauthorized) {
        // Session cookie expired mid-game; log in again and retry once
        session.InvalidateSession();
        if (await session.EnsureAuthenticated()) {
          response.Dispose();
          response = await MakeChatRequest();
        }
      }

      if (!response.IsSuccessStatusCode) {
        error = $"Jun webapp returned {(int)response.StatusCode} ({response.StatusCode})";
      }
    } catch (Exception ex) {
      error = ex.Message;
    }

    var translator = new JunActionTranslator();
    var rawResponseBuilder = new StringBuilder(2048);

    if (error == null) {
      var responseStream = await response.Content.ReadAsStreamAsync();
      using var reader = new StreamReader(responseStream);

      while (!reader.EndOfStream) {
        string cleanText = null;

        try {
          var line = await reader.ReadLineAsync();
          if (string.IsNullOrWhiteSpace(line)) continue;
          if (!line.StartsWith("data: ")) continue;

          var payload = line[6..];
          if (payload == "[DONE]") break;

          var chunk = JsonSerializer.Deserialize<ChatEvent>(payload);

          if (!string.IsNullOrEmpty(chunk?.Error)) {
            error = chunk.Error;
            break;
          }

          // debug/thinking/stats events carry no Token and are skipped
          if (!string.IsNullOrEmpty(chunk?.Token)) {
            rawResponseBuilder.Append(chunk.Token);
            cleanText = translator.Process(chunk.Token);
          }
        } catch (Exception ex) {
          logger.LogError($"Parse error: {ex.Message}");
        }

        if (!string.IsNullOrEmpty(cleanText)) {
          yield return cleanText;
        }
      }
    }

    if (error != null) {
      RemoveLastMessage();
      yield return $"Error: {error}";
      yield break;
    }

    var trailing = translator.Flush();
    if (!string.IsNullOrEmpty(trailing)) {
      yield return trailing;
    }

    // History keeps the raw text (tags included) - it matches what the server
    // stores, so the model sees a consistent conversation next turn
    messages.Add(new ChatMessage {
      Role = "assistant",
      Content = rawResponseBuilder.ToString()
    });
  }

  private async Task<HttpResponseMessage> MakeChatRequest() {
    var payload = new Dictionary<string, object> {
      { "conversation_id", conversationId },
      { "messages", GetHistoryForRequest() },
      { "reasoning", NormalizeReasoning(session.Config.Reasoning) },
      { "client_time", DateTime.Now.ToString("dddd, MMMM d, yyyy 'at' h:mm tt") }
    };

    if (!string.IsNullOrEmpty(session.Config.Model)) {
      payload["model"] = session.Config.Model;
    }

    var request = new HttpRequestMessage(HttpMethod.Post, "api/chat.php") {
      Content = new StringContent(
        JsonSerializer.Serialize(payload),
        Encoding.UTF8,
        "application/json"
      )
    };

    return await session.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
  }

  private List<ChatMessage> GetHistoryForRequest() {
    var nonSystem = GetNonSystemMessagesSnapshot();

    if (nonSystem.Count > maxHistoryMessages) {
      nonSystem = nonSystem.GetRange(nonSystem.Count - maxHistoryMessages, maxHistoryMessages);
    }

    return nonSystem;
  }

  private async Task<bool> EnsureConversation() {
    // Explicit id from the config wins (lets game/web share one chat)
    var configuredId = session.Config.ConversationId;
    var candidateId = configuredId > 0
      ? configuredId
      : SaveStorage.Instance.GetValue(conversationSaveKey, 0);

    if (candidateId > 0 && await ConversationExists(candidateId)) {
      conversationId = candidateId;
      return true;
    }

    // Create a fresh conversation and remember it in the save file
    try {
      var response = await session.Client.PostAsync("api/conversations.php?action=create", null);
      if (!response.IsSuccessStatusCode) {
        logger.LogError($"Failed to create conversation: {response.StatusCode}");
        return false;
      }

      var json = await response.Content.ReadAsStringAsync();
      var created = JsonSerializer.Deserialize<CreatedConversation>(json);

      if (created == null || created.Id <= 0) {
        logger.LogError("Conversation create returned no id");
        return false;
      }

      conversationId = created.Id;
      SaveStorage.Instance.SetValue(conversationSaveKey, conversationId);
      logger.Log($"Created server conversation #{conversationId}");
      return true;
    } catch (Exception ex) {
      logger.LogError($"Failed to create conversation: {ex.Message}");
      return false;
    }
  }

  private async Task<bool> ConversationExists(int id) {
    try {
      var response = await session.Client.GetAsync($"api/conversations.php?action=messages&id={id}");
      return response.IsSuccessStatusCode;
    } catch (Exception) {
      return false;
    }
  }

  /// <summary>
  /// Replaces local history with the server-side conversation,
  /// the copy shared with the web UI
  /// </summary>
  private async Task PullServerHistory() {
    try {
      var response = await session.Client.GetAsync($"api/conversations.php?action=messages&id={conversationId}");
      if (!response.IsSuccessStatusCode) {
        logger.LogWarning($"History pull failed: {response.StatusCode}");
        return;
      }

      var json = await response.Content.ReadAsStringAsync();
      var rows = JsonSerializer.Deserialize<List<ServerMessage>>(json);
      if (rows == null) {
        return;
      }

      var restored = new List<ChatMessage>(rows.Count);
      foreach (var row in rows) {
        if (row == null || string.IsNullOrEmpty(row.Role) || row.Content == null) continue;
        if (row.Role != "user" && row.Role != "assistant") continue;
        restored.Add(new ChatMessage { Role = row.Role, Content = row.Content });
      }

      SetNonSystemMessages(restored);
      logger.Log($"Restored {restored.Count} messages from server conversation #{conversationId}");
    } catch (Exception ex) {
      logger.LogWarning($"History pull failed: {ex.Message}");
    }
  }

  private static string NormalizeReasoning(string value) {
    return (value ?? "auto").ToLowerInvariant() switch {
      "low" => "low",
      "medium" => "medium",
      "high" => "high",
      _ => "auto",
    };
  }

  private class ChatEvent {
    [JsonPropertyName("token")]
    public string Token { get; set; }

    [JsonPropertyName("error")]
    public string Error { get; set; }
  }

  private class CreatedConversation {
    [JsonPropertyName("id")]
    public int Id { get; set; }
  }

  private class ServerMessage {
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
  }
}

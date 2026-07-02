using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MdrgAiDialog.JunApi;

/// <summary>
/// Streaming filter that converts the Jun webapp's action tags into game commands
/// </summary>
/// <remarks>
/// The Jun finetune emits compact action tags like [A:emote|happy] or the legacy
/// [ACTION:brow|emotion=sad] form (they drive the web UI's Live2D rig). The game
/// uses a different rig with the mod's #!bot.* command set, so tags that have a
/// sensible in-game equivalent are translated inline (the ChatParser then executes
/// them like any other command) and everything else is silently dropped.
///
/// The filter is incremental: a tag may arrive split across several stream chunks,
/// so text is held back from the first '[' until the matching ']' (or until the
/// held text can no longer be a tag)
/// </remarks>
public class JunActionTranslator {
  // Longest text we are willing to hold back while waiting for a ']'
  private const int maxTagLength = 300;

  private readonly StringBuilder held = new(64);
  private bool holding = false;

  // emote/brow value -> game expression command
  private static readonly Dictionary<string, string> emoteMap = new(StringComparer.OrdinalIgnoreCase) {
    ["happy"] = "#!bot.Expression.VeryHappy",
    ["excited"] = "#!bot.Expression.VeryHappy",
    ["laughing"] = "#!bot.Expression.VeryHappy",
    ["smug"] = "#!bot.Expression.Happy",
    ["sad"] = "#!bot.Expression.VerySad",
    ["crying"] = "#!bot.Expression.VerySad",
    ["sleepy"] = "#!bot.Expression.Sad",
    ["worried"] = "#!bot.Expression.Sad",
    ["angry"] = "#!bot.Expression.VeryAngry",
    ["pout"] = "#!bot.Expression.Angry",
    ["surprised"] = "#!bot.Expression.VeryShock",
    ["shocked"] = "#!bot.Expression.VeryShock",
    ["embarrassed"] = "#!bot.Expression.VeryBlush",
    ["neutral"] = "#!bot.Expression.Clear",
  };

  /// <summary>
  /// Processes one streamed chunk and returns the text to pass on
  /// (tags removed, translated commands inserted in place)
  /// </summary>
  /// <param name="chunk">Raw streamed text</param>
  public string Process(string chunk) {
    if (string.IsNullOrEmpty(chunk)) {
      return "";
    }

    var output = new StringBuilder(chunk.Length);

    foreach (var character in chunk) {
      if (!holding) {
        if (character == '[') {
          holding = true;
          held.Clear();
          held.Append(character);
        } else {
          output.Append(character);
        }
        continue;
      }

      held.Append(character);

      if (character == ']') {
        output.Append(TranslateTag(held.ToString()));
        held.Clear();
        holding = false;
      } else if (held.Length >= maxTagLength) {
        // Too long to be a tag; it was regular text after all
        output.Append(held);
        held.Clear();
        holding = false;
      }
    }

    return output.ToString();
  }

  /// <summary>
  /// Flushes any text still held back (call once at end of stream)
  /// </summary>
  public string Flush() {
    if (!holding || held.Length == 0) {
      Reset();
      return "";
    }

    var rest = held.ToString();
    Reset();
    return rest;
  }

  /// <summary>
  /// Resets the filter for a new message
  /// </summary>
  public void Reset() {
    held.Clear();
    holding = false;
  }

  /// <summary>
  /// Removes action tags from a complete (non-streamed) text, e.g. stored history
  /// </summary>
  public static string StripTags(string text) {
    if (string.IsNullOrEmpty(text)) {
      return text;
    }

    return System.Text.RegularExpressions.Regex
      .Replace(text, @"\[\s*A(?:CTIONS?)?\s*:[^\]]*\]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
      .Trim();
  }

  private static string TranslateTag(string tag) {
    // tag includes the surrounding brackets: [A:name|value|...] or [ACTION:name|key=value|...]
    var inner = tag.Substring(1, tag.Length - 2).Trim();

    var colonIndex = inner.IndexOf(':');
    if (colonIndex < 0) {
      // Not an action tag ("[laughs]" and similar stay visible text)
      return tag;
    }

    var marker = inner[..colonIndex].Trim();
    var isAction = marker.Equals("A", StringComparison.OrdinalIgnoreCase)
      || marker.Equals("ACTION", StringComparison.OrdinalIgnoreCase)
      || marker.Equals("ACTIONS", StringComparison.OrdinalIgnoreCase);

    if (!isAction) {
      return tag;
    }

    var parts = inner[(colonIndex + 1)..].Split('|');
    var name = parts[0].Trim().ToLowerInvariant();

    var command = name switch {
      "emote" => MapEmote(GetValue(parts, 1, "type")),
      "brow" => MapEmote(GetValue(parts, 1, "emotion")),
      "blush" => MapBlush(GetValue(parts, 1, "intensity")),
      _ => "", // No in-game equivalent; drop silently
    };

    // Commands are space-terminated for the ChatParser
    return command.Length > 0 ? $"{command} " : "";
  }

  private static string MapEmote(string value) {
    if (value != null && emoteMap.TryGetValue(value, out var command)) {
      return command;
    }
    return "";
  }

  private static string MapBlush(string value) {
    var intensity = 0.5;
    if (value != null && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
      intensity = parsed;
    }
    return intensity >= 0.7 ? "#!bot.Expression.VeryBlush" : "#!bot.Expression.Blush";
  }

  /// <summary>
  /// Reads a positional or key=value argument from a tag
  /// </summary>
  private static string GetValue(string[] parts, int position, string key) {
    if (parts.Length <= position) {
      return null;
    }

    // Prefer an explicit key=value anywhere in the tag (legacy form)
    foreach (var part in parts) {
      var eq = part.IndexOf('=');
      if (eq > 0 && part[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) {
        return part[(eq + 1)..].Trim();
      }
    }

    var positional = parts[position].Trim();
    return positional.Contains('=') ? null : positional;
  }
}

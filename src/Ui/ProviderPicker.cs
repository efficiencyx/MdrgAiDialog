using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppTMPro;
using MdrgAiDialog.Chat;
using MdrgAiDialog.Utils;
// Disambiguate from a potential Il2Cpp.Logger
using Logger = MdrgAiDialog.Utils.Logger;

namespace MdrgAiDialog.Ui;

/// <summary>
/// First-run AI-provider picker. Built as a custom uGUI panel (dark card grid) because the game
/// has no reusable, mod-populatable grid UI (its shop is bound to the economy, its mod tools are
/// texture-authoring), and its IL2CPP build strips Unity IMGUI. Selecting a card opens
/// <see cref="ProviderSettingsPanel"/> to configure that provider (model, connection, options).
/// </summary>
public static class ProviderPicker {
  private static readonly Logger logger = new("ProviderPicker");

  internal sealed class Info {
    public string Name;
    public string Label;
    public string Blurb;
  }

  internal static readonly Info[] Providers = [
    new() { Name = "Ollama",     Label = "Ollama",     Blurb = "Local · private · free" },
    new() { Name = "OpenAI",     Label = "OpenAI",     Blurb = "GPT models" },
    new() { Name = "OpenRouter", Label = "OpenRouter", Blurb = "Many models · free tiers" },
    new() { Name = "Claude",     Label = "Claude",     Blurb = "Anthropic" },
    new() { Name = "Google",     Label = "Gemini",     Blurb = "Google" },
    new() { Name = "DeepSeek",   Label = "DeepSeek",   Blurb = "deepseek-chat" },
    new() { Name = "Mistral",    Label = "Mistral",    Blurb = "mistral-small" },
    new() { Name = "Jun",        Label = "Jun webapp", Blurb = "Voice · shared chat" },
    new() { Name = "Mock",       Label = "Mock",       Blurb = "Offline test" },
  ];

  private static GameObject root;

  /// <summary>Builds and shows the picker panel. No-op if already open.</summary>
  public static void Show() {
    if (root != null) {
      return;
    }
    try {
      BuildPanel();
    } catch (Exception ex) {
      logger.LogError($"Failed to build provider picker panel: {ex}");
      Close();
    }
  }

  internal static void Close() {
    if (root != null) {
      UnityEngine.Object.Destroy(root);
      root = null;
    }
  }

  // --- uGUI construction ------------------------------------------------------

  private static void BuildPanel() {
    root = new GameObject("MdrgProviderPicker");
    UnityEngine.Object.DontDestroyOnLoad(root);

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 30000;
    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;
    root.AddComponent<GraphicRaycaster>();

    // Dimming backdrop that also blocks clicks to the menu behind.
    var backdrop = UiKit.NewUi("Backdrop", root.transform);
    UiKit.Stretch(backdrop.GetComponent<RectTransform>());
    backdrop.AddComponent<Image>().color = UiKit.Backdrop;

    // Centered panel.
    var panel = UiKit.NewUi("Panel", root.transform);
    var prt = panel.GetComponent<RectTransform>();
    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
    prt.sizeDelta = new Vector2(940f, 660f);
    prt.anchoredPosition = Vector2.zero;
    panel.AddComponent<Image>().color = UiKit.Panel;

    // Title + subtitle.
    var title = UiKit.NewUi("Title", panel.transform);
    var trt = title.GetComponent<RectTransform>();
    trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
    trt.sizeDelta = new Vector2(-48f, 46f); trt.anchoredPosition = new Vector2(0f, -26f);
    UiKit.AddText(title, "Choose your companion's AI", 34, FontStyles.Bold, UiKit.TextBright);

    var sub = UiKit.NewUi("Subtitle", panel.transform);
    var srt = sub.GetComponent<RectTransform>();
    srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f);
    srt.sizeDelta = new Vector2(-48f, 26f); srt.anchoredPosition = new Vector2(0f, -74f);
    UiKit.AddText(sub, "Pick which AI powers the conversations. Change it later in the config, or press F7.",
      18, FontStyles.Normal, UiKit.TextSub);

    // Grid of provider cards.
    var grid = UiKit.NewUi("Grid", panel.transform);
    var grt = grid.GetComponent<RectTransform>();
    grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
    grt.offsetMin = new Vector2(28f, 80f);   // left, bottom
    grt.offsetMax = new Vector2(-28f, -108f); // right, top
    var glg = grid.AddComponent<GridLayoutGroup>();
    glg.cellSize = new Vector2(282f, 82f);
    glg.spacing = new Vector2(14f, 14f);
    glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    glg.constraintCount = 3;
    glg.childAlignment = TextAnchor.UpperCenter;

    foreach (var p in Providers) {
      AddCard(grid.transform, p);
    }

    // Bottom "Maybe later" button.
    var later = UiKit.Button(panel.transform, "Maybe later", 18, UiKit.Btn, UiKit.BtnHover, UiKit.BtnPress,
      () => Close());
    var lrt = later.GetComponent<RectTransform>();
    lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f); lrt.pivot = new Vector2(0.5f, 0f);
    lrt.sizeDelta = new Vector2(220f, 44f); lrt.anchoredPosition = new Vector2(0f, 22f);
  }

  private static void AddCard(Transform parent, Info info) {
    var card = UiKit.NewUi("Card_" + info.Name, parent);
    var img = card.AddComponent<Image>();
    img.color = UiKit.Card;
    var btn = card.AddComponent<Button>();
    UiKit.Tint(btn, UiKit.Card, UiKit.CardHover, UiKit.CardPress);
    var captured = info.Name;
    btn.onClick.AddListener((UnityAction)(() => OnPicked(captured)));

    var name = UiKit.NewUi("Name", card.transform);
    var nrt = name.GetComponent<RectTransform>();
    nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 1f);
    nrt.offsetMin = new Vector2(10f, 34f); nrt.offsetMax = new Vector2(-10f, -8f);
    UiKit.AddText(name, info.Label, 22, FontStyles.Bold, UiKit.TextBright);

    var blurb = UiKit.NewUi("Blurb", card.transform);
    var brt = blurb.GetComponent<RectTransform>();
    brt.anchorMin = new Vector2(0f, 0f); brt.anchorMax = new Vector2(1f, 0f); brt.pivot = new Vector2(0.5f, 0f);
    brt.sizeDelta = new Vector2(-16f, 28f); brt.anchoredPosition = new Vector2(0f, 8f);
    UiKit.AddText(blurb, info.Blurb, 14, FontStyles.Normal, UiKit.TextDim);
  }

  // --- Selection flow ---------------------------------------------------------

  private static void OnPicked(string name) {
    Close();
    if (name == "Mock") {
      // Nothing to configure for the offline mock provider.
      Finalize(name);
      return;
    }
    ProviderSettingsPanel.Open(name);
  }

  /// <summary>Re-opens the provider grid (used by the settings panel's Back button).</summary>
  internal static void ReopenGrid() {
    Close();
    Show();
  }

  /// <summary>
  /// Persists the chosen provider as the active one and applies it live. Called by
  /// <see cref="ProviderSettingsPanel"/> after it has written the provider's settings.
  /// </summary>
  internal static void Finalize(string name) {
    try {
      ModConfig.SetUsedProvider(name);
      ModConfig.MarkProviderConfigured();
      ModConfig.Save();
      ChatManager.Instance?.RebuildAdapter();
      logger.Log($"Provider set to '{name}' via first-run picker.");
      Il2Cpp.UiOverlay.Instance?.OkPopupSuccess(
        $"AI provider set to {name}.\nOpen the chat to talk to your companion!", null);
    } catch (Exception ex) {
      logger.LogError($"Failed to save provider '{name}': {ex}");
      Il2Cpp.UiOverlay.Instance?.OkPopupError($"Could not save provider settings:\n{ex.Message}", null);
    }
  }
}

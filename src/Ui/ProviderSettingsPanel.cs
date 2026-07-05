using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppTMPro;
using MdrgAiDialog.JunApi;
using MdrgAiDialog.Utils;
using Logger = MdrgAiDialog.Utils.Logger;

namespace MdrgAiDialog.Ui;

/// <summary>
/// Per-provider settings sub-panel shown after a provider is picked. Exposes everything the
/// provider lets you change — model (live-fetched list + custom entry), connection, reasoning,
/// and the sampling/timeout knobs — then saves and activates the provider. Text/number fields
/// are edited through the game's native <c>InputPopup</c> (the IL2CPP build has no usable inline
/// text input); everything else is buttons, styled with <see cref="UiKit"/>.
/// </summary>
public static class ProviderSettingsPanel {
  private static readonly Logger logger = new("ProviderSettings");

  private sealed class Working {
    public string ApiUrl;
    public string ApiKey;
    public string Model;
    public string Reasoning;
    public double Temperature;
    public int TopK;
    public int TimeoutSeconds;
    public bool ReasoningPreFill;
    // Jun-only
    public string Email;
    public string Password;
    public int ConversationId;
    // Jun-only TTS ("Voice")
    public bool TtsEnabled;
    public string TtsVoice;
    public string TtsEngine;
    public double TtsSpeed;
    public double TtsVolume;
    public bool TtsLipSync;
  }

  private static readonly string[] StdReasoning = ["Auto", "Enabled", "Disabled"];
  private static readonly string[] JunReasoning = ["Auto", "Low", "Medium", "High"];
  private static readonly string[] TtsEngines = ["kokoro", "pockettts"];
  private const int MaxModelChips = 16;

  private static GameObject root;
  private static Transform content;
  private static string provider;
  private static bool isJun;
  private static Working w;
  private static List<string> models;   // null = loading, empty = failed/none
  private static int fetchToken;

  /// <summary>Opens the settings panel for a provider. Replaces any panel already open.</summary>
  public static void Open(string providerName) {
    Close();
    provider = providerName;
    isJun = providerName == "Jun";
    w = Load(providerName);
    models = null;
    try {
      Build();
      RefreshModels();
    } catch (Exception ex) {
      logger.LogError($"Failed to build settings panel: {ex}");
      Close();
      ProviderPicker.ReopenGrid();
    }
  }

  private static void Close() {
    if (root != null) {
      UnityEngine.Object.Destroy(root);
      root = null;
      content = null;
    }
  }

  private static string Label => Array.Find(ProviderPicker.Providers, p => p.Name == provider)?.Label ?? provider;

  private static Working Load(string name) {
    if (name == "Jun") {
      var jc = ModConfig.GetJunView();
      var tc = ModConfig.GetTtsConfig();
      return new Working {
        ApiUrl = jc.ApiUrl, Email = jc.Email, Password = jc.Password,
        Model = jc.Model, Reasoning = jc.Reasoning, ConversationId = jc.ConversationId,
        TimeoutSeconds = jc.TimeoutSeconds,
        TtsEnabled = tc.Enabled, TtsVoice = tc.Voice, TtsEngine = tc.Engine,
        TtsSpeed = tc.Speed, TtsVolume = tc.Volume, TtsLipSync = tc.LipSync,
      };
    }
    var v = ModConfig.GetProviderView(name);
    return new Working {
      ApiUrl = v.ApiUrl, ApiKey = v.ApiKey, Model = v.Model, Reasoning = v.Reasoning,
      Temperature = v.Temperature, TopK = v.TopK, TimeoutSeconds = v.TimeoutSeconds,
      ReasoningPreFill = v.ReasoningPreFill,
    };
  }

  // --- shell (built once) -----------------------------------------------------

  private static void Build() {
    root = new GameObject("MdrgProviderSettings");
    UnityEngine.Object.DontDestroyOnLoad(root);

    var canvas = root.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.sortingOrder = 30001;
    var scaler = root.AddComponent<CanvasScaler>();
    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
    scaler.referenceResolution = new Vector2(1920f, 1080f);
    scaler.matchWidthOrHeight = 0.5f;
    root.AddComponent<GraphicRaycaster>();

    var backdrop = UiKit.NewUi("Backdrop", root.transform);
    UiKit.Stretch(backdrop.GetComponent<RectTransform>());
    backdrop.AddComponent<Image>().color = UiKit.Backdrop;

    var panel = UiKit.NewUi("Panel", root.transform);
    var prt = panel.GetComponent<RectTransform>();
    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
    prt.sizeDelta = new Vector2(880f, 760f);
    prt.anchoredPosition = Vector2.zero;
    panel.AddComponent<Image>().color = UiKit.Panel;

    var title = UiKit.NewUi("Title", panel.transform);
    var trt = title.GetComponent<RectTransform>();
    trt.anchorMin = new Vector2(0f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
    trt.sizeDelta = new Vector2(-48f, 44f); trt.anchoredPosition = new Vector2(0f, -24f);
    UiKit.AddText(title, $"{Label} settings", 30, FontStyles.Bold, UiKit.TextBright);

    var sub = UiKit.NewUi("Subtitle", panel.transform);
    var srt = sub.GetComponent<RectTransform>();
    srt.anchorMin = new Vector2(0f, 1f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(0.5f, 1f);
    srt.sizeDelta = new Vector2(-48f, 24f); srt.anchoredPosition = new Vector2(0f, -66f);
    UiKit.AddText(sub, "Choose a model and adjust anything you like, then save.",
      17, FontStyles.Normal, UiKit.TextSub);

    // Scrollable settings area.
    var scroll = UiKit.NewUi("Scroll", panel.transform);
    var scrt = scroll.GetComponent<RectTransform>();
    scrt.anchorMin = Vector2.zero; scrt.anchorMax = Vector2.one;
    scrt.offsetMin = new Vector2(26f, 78f);    // leave room for bottom bar
    scrt.offsetMax = new Vector2(-26f, -100f);  // leave room for title
    var sr = scroll.AddComponent<ScrollRect>();
    sr.horizontal = false;
    sr.vertical = true;
    sr.movementType = ScrollRect.MovementType.Clamped;
    sr.scrollSensitivity = 26f;

    var viewport = UiKit.NewUi("Viewport", scroll.transform);
    UiKit.Stretch(viewport.GetComponent<RectTransform>());
    viewport.AddComponent<RectMask2D>();
    var vpImg = viewport.AddComponent<Image>();
    vpImg.color = new Color(0f, 0f, 0f, 0.001f); // near-invisible, needed so the mask has graphics
    sr.viewport = viewport.GetComponent<RectTransform>();

    var contentGo = UiKit.NewUi("Content", viewport.transform);
    var crt = contentGo.GetComponent<RectTransform>();
    crt.anchorMin = new Vector2(0f, 1f); crt.anchorMax = new Vector2(1f, 1f); crt.pivot = new Vector2(0.5f, 1f);
    // sizeDelta.x MUST be 0 so a horizontally-stretched content exactly matches the viewport width;
    // otherwise the default sizeDelta makes it overhang and the RectMask2D clips the left column.
    crt.sizeDelta = Vector2.zero;
    crt.anchoredPosition = Vector2.zero;
    crt.offsetMin = new Vector2(0f, crt.offsetMin.y);
    crt.offsetMax = new Vector2(0f, crt.offsetMax.y);
    var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
    vlg.spacing = 8f;
    vlg.padding = new RectOffset(4, 12, 4, 8);
    vlg.childControlWidth = true;
    vlg.childControlHeight = true;
    vlg.childForceExpandWidth = true;
    vlg.childForceExpandHeight = false;
    var fitter = contentGo.AddComponent<ContentSizeFitter>();
    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    sr.content = crt;
    content = contentGo.transform;

    // Bottom bar: Back (left), Save & use (right).
    var back = UiKit.Button(panel.transform, "‹ Back", 18, UiKit.Btn, UiKit.BtnHover, UiKit.BtnPress,
      () => { Close(); ProviderPicker.ReopenGrid(); });
    var brt = back.GetComponent<RectTransform>();
    brt.anchorMin = brt.anchorMax = new Vector2(0f, 0f); brt.pivot = new Vector2(0f, 0f);
    brt.sizeDelta = new Vector2(150f, 46f); brt.anchoredPosition = new Vector2(26f, 20f);

    var save = UiKit.Button(panel.transform, "Save & use", 19, UiKit.Confirm, UiKit.ConfirmHover, UiKit.ConfirmPress,
      () => Commit(), UiKit.TextBright);
    var svrt = save.GetComponent<RectTransform>();
    svrt.anchorMin = svrt.anchorMax = new Vector2(1f, 0f); svrt.pivot = new Vector2(1f, 0f);
    svrt.sizeDelta = new Vector2(220f, 46f); svrt.anchoredPosition = new Vector2(-26f, 20f);

    Rebuild();
  }

  // --- content (rebuilt on every change) --------------------------------------

  private static void Rebuild() {
    if (content == null) {
      return;
    }
    for (int i = content.childCount - 1; i >= 0; i--) {
      UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
    }

    SectionHeader("Model");
    ModelSection();

    SectionHeader("Connection");
    if (isJun) {
      ValueRow("Server URL", Display(w.ApiUrl, "https://your-host"),
        () => OpenInput("Jun server URL", "Base URL of your Jun webapp:", w.ApiUrl,
          TMP_InputField.ContentType.Standard, v => w.ApiUrl = v));
      ValueRow("Email", Display(w.Email, "(not set)"),
        () => OpenInput("Jun email", "Account email:", w.Email,
          TMP_InputField.ContentType.EmailAddress, v => w.Email = v));
      ValueRow("Password", Mask(w.Password),
        () => OpenInput("Jun password", "Account password:", w.Password,
          TMP_InputField.ContentType.Password, v => w.Password = v));
    } else {
      ValueRow("API URL", Display(w.ApiUrl, "(default)"),
        () => OpenInput($"{Label} API URL", "Server URL:", w.ApiUrl,
          TMP_InputField.ContentType.Standard, v => w.ApiUrl = v));
      ValueRow("API key", Mask(w.ApiKey),
        () => OpenInput($"{Label} API key", "Paste your API key:", w.ApiKey,
          TMP_InputField.ContentType.Standard, v => w.ApiKey = v));
    }

    SectionHeader("Options");
    SegmentRow("Reasoning", isJun ? JunReasoning : StdReasoning, w.Reasoning, v => { w.Reasoning = v; Rebuild(); });

    if (!isJun) {
      ToggleRow("Prefill <think> tag", w.ReasoningPreFill, () => { w.ReasoningPreFill = !w.ReasoningPreFill; Rebuild(); });
      ValueRow("Temperature", w.Temperature.ToString("0.0#", CultureInfo.InvariantCulture),
        () => OpenInput("Temperature", "0.0 – 2.0:", w.Temperature.ToString("0.0#", CultureInfo.InvariantCulture),
          TMP_InputField.ContentType.DecimalNumber, s => {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
              w.Temperature = Math.Clamp(d, 0.0, 2.0);
            }
          }));
      ValueRow("Top-K", w.TopK.ToString(),
        () => OpenInput("Top-K", "Sampling Top-K (-1 disables):", w.TopK.ToString(),
          TMP_InputField.ContentType.IntegerNumber, s => {
            if (int.TryParse(s, out var k)) w.TopK = k;
          }));
    } else {
      ValueRow("Conversation ID", w.ConversationId == 0 ? "0 (auto)" : w.ConversationId.ToString(),
        () => OpenInput("Conversation ID", "Server conversation to continue (0 = auto):", w.ConversationId.ToString(),
          TMP_InputField.ContentType.IntegerNumber, s => {
            if (int.TryParse(s, out var id) && id >= 0) w.ConversationId = id;
          }));
    }

    ValueRow("Timeout (s)", w.TimeoutSeconds.ToString(),
      () => OpenInput("Timeout", "Request timeout in seconds:", w.TimeoutSeconds.ToString(),
        TMP_InputField.ContentType.IntegerNumber, s => {
          if (int.TryParse(s, out var t) && t > 0) w.TimeoutSeconds = t;
        }));

    // The Jun webapp bundles a TTS endpoint, so voice playback needs no extra server/key.
    if (isJun) {
      TtsSection();
    }
  }

  private static void TtsSection() {
    SectionHeader("Voice (TTS)");
    ToggleRow("Speak replies out loud", w.TtsEnabled, () => { w.TtsEnabled = !w.TtsEnabled; Rebuild(); });

    if (!w.TtsEnabled) {
      return;
    }

    SegmentRow("Engine", TtsEngines, w.TtsEngine, v => { w.TtsEngine = v; Rebuild(); });
    ValueRow("Voice", Display(w.TtsVoice, "af_heart"),
      () => OpenInput("Voice", "Voice name (e.g. Kokoro af_heart):", w.TtsVoice,
        TMP_InputField.ContentType.Standard, v => w.TtsVoice = v));
    ValueRow("Speed", w.TtsSpeed.ToString("0.0#", CultureInfo.InvariantCulture),
      () => OpenInput("Speed", "Speech speed 0.5 – 2.0:", w.TtsSpeed.ToString("0.0#", CultureInfo.InvariantCulture),
        TMP_InputField.ContentType.DecimalNumber, s => {
          if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
            w.TtsSpeed = Math.Clamp(d, 0.5, 2.0);
          }
        }));
    ValueRow("Volume (gain)", w.TtsVolume.ToString("0.0#", CultureInfo.InvariantCulture),
      () => OpenInput("Volume", "Playback gain 0.0 – 5.0 (1 = original, higher = louder):", w.TtsVolume.ToString("0.0#", CultureInfo.InvariantCulture),
        TMP_InputField.ContentType.DecimalNumber, s => {
          if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) {
            w.TtsVolume = Math.Clamp(d, 0.0, 5.0);
          }
        }));
    ToggleRow("Lip-sync", w.TtsLipSync, () => { w.TtsLipSync = !w.TtsLipSync; Rebuild(); });
  }

  private static void SectionHeader(string text) {
    var row = MakeRow(30f);
    var t = UiKit.NewUi("Header", row.transform);
    var rt = t.GetComponent<RectTransform>();
    rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 1f);
    rt.offsetMin = new Vector2(6f, 0f); rt.offsetMax = new Vector2(-6f, 0f);
    var txt = UiKit.AddText(t, text.ToUpperInvariant(), 15, FontStyles.Bold, UiKit.CardHover, TextAlignmentOptions.MidlineLeft);
    txt.characterSpacing = 6f;
  }

  private static void ModelSection() {
    // GridLayoutGroup already reports its preferred height as an ILayoutElement, which the parent
    // VerticalLayoutGroup reads — so no ContentSizeFitter here (it would fight the parent's sizing).
    var container = UiKit.NewUi("Models", content);
    var grid = container.AddComponent<GridLayoutGroup>();
    grid.cellSize = new Vector2(258f, 38f);
    grid.spacing = new Vector2(10f, 8f);
    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
    grid.constraintCount = 3;
    grid.childAlignment = TextAnchor.UpperLeft;

    if (models == null) {
      Chip(container.transform, "Loading models…", false, null);
      return;
    }

    var shown = new List<string>();
    if (!string.IsNullOrWhiteSpace(w.Model)) shown.Add(w.Model);   // keep the current pick visible/first
    foreach (var m in (models.Count > 0 ? models : ModelCatalog.Curated(provider))) {
      if (!shown.Contains(m)) shown.Add(m);
      if (shown.Count >= MaxModelChips) break;
    }
    if (shown.Count == 0) shown.Add("(server default)");

    foreach (var m in shown) {
      var captured = m;
      bool selected = m == w.Model || (string.IsNullOrWhiteSpace(w.Model) && m == "(server default)");
      Chip(container.transform, m, selected, () => { w.Model = m == "(server default)" ? "" : captured; Rebuild(); });
    }

    Chip(container.transform, "＋ Custom…", false,
      () => OpenInput("Custom model", "Type the exact model id:", w.Model,
        TMP_InputField.ContentType.Standard, v => w.Model = v));
    Chip(container.transform, "↻ Refresh", false, () => RefreshModels());
  }

  private static void Chip(Transform parent, string label, bool selected, Action onClick) {
    Color fill = selected ? UiKit.Selected : UiKit.Card;
    var chip = UiKit.NewUi("Chip", parent);
    var img = chip.AddComponent<Image>();
    img.color = fill;
    if (onClick != null) {
      var btn = chip.AddComponent<Button>();
      UiKit.Tint(btn, fill, UiKit.CardHover, UiKit.CardPress);
      btn.onClick.AddListener((UnityAction)(() => onClick()));
    }
    var lbl = UiKit.NewUi("Label", chip.transform);
    var lrt = lbl.GetComponent<RectTransform>();
    lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
    lrt.offsetMin = new Vector2(10f, 2f); lrt.offsetMax = new Vector2(-10f, -2f);
    var t = UiKit.AddText(lbl, label, 15, selected ? FontStyles.Bold : FontStyles.Normal, UiKit.TextBright, TextAlignmentOptions.Left);
    t.enableWordWrapping = false;
    t.raycastTarget = false;
  }

  // Generic label + value + Edit-button row.
  private static void ValueRow(string label, string value, Action onEdit) {
    var row = MakeRow(46f);

    var l = UiKit.NewUi("Label", row.transform);
    var lrt = l.GetComponent<RectTransform>();
    lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0.32f, 1f);
    lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(0f, 0f);
    var lt = UiKit.AddText(l, label, 17, FontStyles.Normal, UiKit.TextLabel, TextAlignmentOptions.MidlineLeft);
    lt.raycastTarget = false;

    var v = UiKit.NewUi("Value", row.transform);
    var vrt = v.GetComponent<RectTransform>();
    vrt.anchorMin = new Vector2(0.32f, 0f); vrt.anchorMax = new Vector2(1f, 1f);
    vrt.offsetMin = new Vector2(6f, 0f); vrt.offsetMax = new Vector2(-108f, 0f);
    var vt = UiKit.AddText(v, value, 16, FontStyles.Normal, UiKit.TextDim, TextAlignmentOptions.MidlineRight);
    vt.enableWordWrapping = false;
    vt.raycastTarget = false;

    var edit = UiKit.Button(row.transform, "Edit", 15, UiKit.Btn, UiKit.BtnHover, UiKit.BtnPress, onEdit);
    var ert = edit.GetComponent<RectTransform>();
    ert.anchorMin = new Vector2(1f, 0.5f); ert.anchorMax = new Vector2(1f, 0.5f); ert.pivot = new Vector2(1f, 0.5f);
    ert.sizeDelta = new Vector2(92f, 34f); ert.anchoredPosition = new Vector2(-8f, 0f);
  }

  // Label + a row of mutually-exclusive option buttons.
  private static void SegmentRow(string label, string[] options, string selected, Action<string> onSelect) {
    var row = MakeRow(46f);

    var l = UiKit.NewUi("Label", row.transform);
    var lrt = l.GetComponent<RectTransform>();
    lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0.32f, 1f);
    lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(0f, 0f);
    UiKit.AddText(l, label, 17, FontStyles.Normal, UiKit.TextLabel, TextAlignmentOptions.MidlineLeft).raycastTarget = false;

    var seg = UiKit.NewUi("Seg", row.transform);
    var srt = seg.GetComponent<RectTransform>();
    srt.anchorMin = new Vector2(0.32f, 0.5f); srt.anchorMax = new Vector2(1f, 0.5f); srt.pivot = new Vector2(0f, 0.5f);
    srt.sizeDelta = new Vector2(0f, 34f); srt.offsetMin = new Vector2(6f, -17f); srt.offsetMax = new Vector2(-8f, 17f);
    var hlg = seg.AddComponent<HorizontalLayoutGroup>();
    hlg.spacing = 6f;
    hlg.childControlWidth = true; hlg.childControlHeight = true;
    hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = true;

    foreach (var opt in options) {
      bool on = string.Equals(opt, selected, StringComparison.OrdinalIgnoreCase);
      var captured = opt;
      UiKit.Button(seg.transform, opt, 15, on ? UiKit.Selected : UiKit.Btn, UiKit.BtnHover, UiKit.BtnPress,
        () => onSelect(captured), on ? UiKit.TextBright : UiKit.TextLabel);
    }
  }

  private static void ToggleRow(string label, bool value, Action onToggle) {
    var row = MakeRow(46f);

    var l = UiKit.NewUi("Label", row.transform);
    var lrt = l.GetComponent<RectTransform>();
    lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0.7f, 1f);
    lrt.offsetMin = new Vector2(10f, 0f); lrt.offsetMax = new Vector2(0f, 0f);
    UiKit.AddText(l, label, 17, FontStyles.Normal, UiKit.TextLabel, TextAlignmentOptions.MidlineLeft).raycastTarget = false;

    var tog = UiKit.Button(row.transform, value ? "On" : "Off", 15,
      value ? UiKit.Selected : UiKit.Btn, UiKit.BtnHover, UiKit.BtnPress, onToggle,
      value ? UiKit.TextBright : UiKit.TextLabel);
    var trt = tog.GetComponent<RectTransform>();
    trt.anchorMin = new Vector2(1f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(1f, 0.5f);
    trt.sizeDelta = new Vector2(92f, 34f); trt.anchoredPosition = new Vector2(-8f, 0f);
  }

  private static GameObject MakeRow(float height) {
    var go = UiKit.NewUi("Row", content);
    var le = go.AddComponent<LayoutElement>();
    le.minHeight = height; le.preferredHeight = height; le.flexibleWidth = 1f;
    var bg = go.AddComponent<Image>();
    bg.color = new Color(1f, 1f, 1f, 0.035f);
    bg.raycastTarget = false;
    return go;
  }

  // --- editing via the game's native input popup ------------------------------

  // The native InputPopup renders on the game's dedicated popup layer, above our overlay canvas,
  // so while it's open we hide our panel entirely — otherwise it covers the popup and eats its
  // clicks. A coroutine restores the panel once the popup is dismissed (works for both OK and
  // cancel, since the popup GameObject is destroyed either way). The apply runs only on OK.
  private static void OpenInput(string title, string body, string current,
      TMP_InputField.ContentType type, Action<string> apply) {
    var ui = Il2Cpp.UiOverlay.Instance;
    if (ui == null) {
      return;
    }
    HidePanel();
    var popup = ui.InputPopup(title, body, new Action<string>(input => {
      var text = (input ?? "").Trim();
      if (text.Length > 0) {
        apply(text);
      }
    }), current ?? "", type);
    MelonCoroutines.Start(RestoreWhenClosed(popup));
  }

  private static IEnumerator RestoreWhenClosed(Il2Cpp.InputPopup popup) {
    yield return null; // let the popup finish opening
    while (popup != null) {
      yield return null; // destroyed on OK or cancel
    }
    ShowPanel();
  }

  private static void HidePanel() {
    if (root != null) {
      root.SetActive(false);
    }
  }

  private static void ShowPanel() {
    if (root != null) {
      root.SetActive(true);
      Rebuild();
    }
  }

  // --- model fetch ------------------------------------------------------------

  private static async void RefreshModels() {
    models = null;
    int token = ++fetchToken;

    // Jun lists models per-account, so the (possibly just-edited) credentials must be live first.
    if (isJun) {
      ModConfig.SetJunConnection(w.ApiUrl, w.Email, w.Password);
      ModConfig.Save();
      JunSession.Reset();
    }
    Rebuild();

    List<string> result;
    try {
      var view = new ModConfig.ProviderView { ApiUrl = w.ApiUrl, ApiKey = w.ApiKey };
      result = await ModelCatalog.FetchAsync(provider, view);
    } catch (Exception ex) {
      logger.LogWarning($"Model refresh failed: {ex.Message}");
      result = [];
    }

    await MainThreadRunner.Run(() => {
      if (token != fetchToken || root == null) {
        return; // superseded by a newer refresh, or the panel closed
      }
      models = result;
      Rebuild();
    });
  }

  // --- save -------------------------------------------------------------------

  private static void Commit() {
    try {
      if (isJun) {
        ModConfig.SetJunConnection(w.ApiUrl, w.Email, w.Password);
        ModConfig.SetJunAdvanced(w.Model, w.Reasoning, w.ConversationId, w.TimeoutSeconds);
        ModConfig.SetTtsForJun(w.TtsEnabled, w.TtsVoice, w.TtsEngine, w.TtsSpeed, w.TtsVolume, w.TtsLipSync);
        JunSession.Reset();
        Tts.TtsManager.Instance?.Reload();
      } else {
        ModConfig.SetProviderConnection(provider, w.ApiUrl, w.ApiKey, w.Model);
        ModConfig.SetProviderAdvanced(provider, w.Temperature, w.TopK, w.Reasoning, w.ReasoningPreFill, w.TimeoutSeconds);
      }
    } catch (Exception ex) {
      logger.LogError($"Failed to write settings for {provider}: {ex}");
    }
    Close();
    ProviderPicker.Finalize(provider);
  }

  // --- display helpers --------------------------------------------------------

  private static string Display(string value, string fallback) =>
    string.IsNullOrWhiteSpace(value) ? fallback : value;

  private static string Mask(string value) =>
    string.IsNullOrEmpty(value) ? "(not set)" : new string('•', Math.Min(12, value.Length));
}

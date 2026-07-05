using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Il2CppTMPro;

namespace MdrgAiDialog.Ui;

/// <summary>
/// Shared uGUI construction helpers and the game-matched palette for the mod's custom panels.
/// Centralised so the first-run picker and its settings sub-panel stay visually identical
/// (one place to tweak the purple).
/// </summary>
internal static class UiKit {
  // Palette sampled from the game's menu purple (sRGB 30,11,46). The game renders UI in gamma
  // space, so 0-1 channels map 1:1 to on-screen sRGB.
  public static readonly Color Panel = new(0.118f, 0.043f, 0.180f, 0.98f);  // srgb(30,11,46)
  public static readonly Color Card = new(0.29f, 0.10f, 0.40f, 1f);         // lighter violet
  public static readonly Color CardHover = new(0.85f, 0.24f, 0.56f, 1f);    // magenta accent
  public static readonly Color CardPress = new(0.64f, 0.16f, 0.42f, 1f);
  public static readonly Color Backdrop = new(0.05f, 0.0f, 0.10f, 0.84f);

  // Secondary button (Maybe later / Back / Edit) — a muted violet.
  public static readonly Color Btn = new(0.30f, 0.12f, 0.42f, 1f);
  public static readonly Color BtnHover = new(0.42f, 0.22f, 0.55f, 1f);
  public static readonly Color BtnPress = new(0.24f, 0.09f, 0.34f, 1f);

  // Green confirm button (Save & use).
  public static readonly Color Confirm = new(0.20f, 0.52f, 0.30f, 1f);
  public static readonly Color ConfirmHover = new(0.28f, 0.66f, 0.40f, 1f);
  public static readonly Color ConfirmPress = new(0.15f, 0.42f, 0.24f, 1f);

  // A selected chip (reasoning segment / current model) — brighter magenta fill.
  public static readonly Color Selected = new(0.72f, 0.20f, 0.48f, 1f);

  public static readonly Color TextBright = Color.white;
  public static readonly Color TextDim = new(0.72f, 0.74f, 0.80f, 1f);
  public static readonly Color TextSub = new(0.70f, 0.72f, 0.78f, 1f);
  public static readonly Color TextLabel = new(0.82f, 0.82f, 0.86f, 1f);

  private static TMP_FontAsset cachedFont;

  public static TMP_FontAsset Font {
    get {
      if (cachedFont != null) {
        return cachedFont;
      }
      var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
      if (fonts != null && fonts.Length > 0) {
        cachedFont = fonts[0];
      }
      return cachedFont;
    }
  }

  public static GameObject NewUi(string name, Transform parent) {
    var go = new GameObject(name);
    var rt = go.AddComponent<RectTransform>();
    rt.SetParent(parent, false);
    return go;
  }

  public static void Stretch(RectTransform rt) {
    rt.anchorMin = Vector2.zero;
    rt.anchorMax = Vector2.one;
    rt.offsetMin = Vector2.zero;
    rt.offsetMax = Vector2.zero;
  }

  public static TextMeshProUGUI AddText(GameObject go, string text, int size, FontStyles style,
      Color color, TextAlignmentOptions align = TextAlignmentOptions.Center) {
    var t = go.AddComponent<TextMeshProUGUI>();
    if (Font != null) t.font = Font;
    t.text = text;
    t.fontSize = size;
    t.fontStyle = style;
    t.color = color;
    t.alignment = align;
    t.enableWordWrapping = true;
    t.overflowMode = TextOverflowModes.Ellipsis;
    return t;
  }

  public static void Tint(Button btn, Color normal, Color hover, Color press) {
    var cb = btn.colors;
    cb.normalColor = normal;
    cb.highlightedColor = hover;
    cb.pressedColor = press;
    cb.selectedColor = normal;
    cb.fadeDuration = 0.08f;
    btn.colors = cb;
  }

  /// <summary>Creates a filled, clickable button with a centered label. Returns its GameObject.</summary>
  public static GameObject Button(Transform parent, string label, int fontSize, Color fill,
      Color hover, Color press, System.Action onClick, Color? textColor = null) {
    var go = NewUi("Btn_" + label, parent);
    var img = go.AddComponent<Image>();
    img.color = fill;
    var btn = go.AddComponent<Button>();
    Tint(btn, fill, hover, press);
    // A bare lambda won't implicitly convert to the Il2Cpp UnityAction; cast at the call to AddListener.
    btn.onClick.AddListener((UnityAction)(() => onClick()));
    var lbl = NewUi("Label", go.transform);
    Stretch(lbl.GetComponent<RectTransform>());
    AddText(lbl, label, fontSize, FontStyles.Normal, textColor ?? TextLabel);
    return go;
  }
}

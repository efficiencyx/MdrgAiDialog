using System;
using System.Collections;
using MelonLoader;
using UnityEngine;
using Il2Cpp;
using MdrgAiDialog.Chat;
using MdrgAiDialog.Ui;
using MdrgAiDialog.Utils;

[assembly: MelonInfo(typeof(MdrgAiDialog.Core), "MdrgAiDialog", "0.4.0", "Delta", null)]
[assembly: MelonGame("IncontinentCell", "My Dystopian Robot Girlfriend")]

namespace MdrgAiDialog;

public class Core : MelonMod {
  /// <summary>
  /// Gets the singleton instance of the plugin
  /// </summary>
  public static Core Instance { get; private set; }

  /// <summary>
  /// Gets the root GameObject of the mod
  /// </summary>
  public GameObject RootObject { get; private set; }

  // Legacy UnityEngine.Input throws when the game routes input through the new Input System;
  // disable the reopen hotkey the first time that happens.
  private bool hotkeyDisabled;
  private bool pickerShown;

  public override void OnInitializeMelon() {
    // Make this instance available to other classes
    Instance = this;

    // Load config
    ModConfig.Load();

    // Create Root GameObject
    RootObject = new GameObject(Guid.NewGuid().ToString());
    UnityEngine.Object.DontDestroyOnLoad(RootObject);

    // Add singletons
    MonoSingletonManager.Add<MainThreadRunner>();
    MonoSingletonManager.Add<SaveStorage>();
    MonoSingletonManager.Add<ChatManager>();
    MonoSingletonManager.Add<ChatWriter>();
    MonoSingletonManager.Add<Tts.TtsManager>();

    // On first run, show the native provider picker once the game UI is up.
    if (!ModConfig.IsProviderConfigured) {
      MelonCoroutines.Start(ShowPickerWhenReady());
    }
  }

  private IEnumerator ShowPickerWhenReady() {
    // The UI overlay only exists once we're in a scene that has the game's UI.
    while (UiOverlay.Instance == null) {
      yield return null;
    }
    // Let the scene settle so the popup lands cleanly on top.
    for (int i = 0; i < 120; i++) {
      yield return null;
    }
    if (!ModConfig.IsProviderConfigured && !pickerShown) {
      pickerShown = true;
      ProviderPicker.Show();
    }
  }

  public override void OnUpdate() {
    if (hotkeyDisabled) {
      return;
    }

    try {
      if (Input.GetKeyDown(KeyCode.F7)) {
        ProviderPicker.Show();
      }
    } catch (Exception) {
      // Game uses the new Input System; the legacy Input API is unavailable.
      hotkeyDisabled = true;
    }
  }
}

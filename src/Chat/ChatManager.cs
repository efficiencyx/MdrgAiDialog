using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using MelonLoader;
using Il2Cpp;
using Il2CppInterop.Runtime.Attributes;
using MdrgAiDialog.Utils;
using Logger = MdrgAiDialog.Utils.Logger;

namespace MdrgAiDialog.Chat;

/// <summary>
/// Manages the chat interaction with the AI, including UI and game state
/// </summary>
[MonoSingleton]
[RegisterTypeInIl2Cpp]
public class ChatManager : MonoBehaviour {
  public static ChatManager Instance => MonoSingletonManager.Get<ChatManager>();

  /// <summary>
  /// Indicates if a chat session is currently active
  /// </summary>
  public bool IsChatActive { get; private set; } = false;
  private bool isInConversation = false;

  private AiAdapter aiAdapter;
  private readonly ChatParser parser;
  private readonly ChatWriter writer;
  private readonly ChatExecutor executor;
  private readonly Action<string> processUserInput;

  private Il2CppFungus.NarrativeLog narrativeLog;
  private InputPopup chatPopup;
  private static readonly Logger logger = new("ChatManager");

  private string chatTitle = "Say something to the bot";
  // Command hints and the paste button are stripped in ConfigureChatPopup, and OK is replaced by
  // Send + Close, so the body text is left empty.
  private readonly string chatDescription = "";

  /// <summary>
  /// Initializes chat components and handlers
  /// </summary>
  public ChatManager() : base() {
    aiAdapter = new AiAdapter();
    writer = ChatWriter.Instance;
    executor = new ChatExecutor(this, writer);
    parser = new ChatParser(writer, executor);
    processUserInput = new(ProcessUserInput);
  }

  /// <summary>
  /// Validates singleton initialization
  /// </summary>
  public void Awake() {
    this.ValidateSingleton();
    SaveStorage.EventBus.AddListener("cache-invalidated", ReloadHistoryFromSave);
    ReloadHistoryFromSave();
  }

  private void ReloadHistoryFromSave() {
    var nonSystem = SaveStorage.Instance.GetValue<List<AiProviders.AiProvider.ChatMessage>>("chat-history", []);
    aiAdapter.RestoreNonSystemMessages(nonSystem);
  }

  private void PersistHistoryToSave() {
    var nonSystem = aiAdapter.GetNonSystemMessagesSnapshot();
    SaveStorage.Instance.SetValue("chat-history", nonSystem);
  }

  /// <summary>
  /// Begins a new chat session
  /// </summary>
  public void StartChat() {
    MelonCoroutines.Start(ChatLoop());
  }

  /// <summary>
  /// Ends the current chat session
  /// </summary>
  /// <param name="waitForInput">If true, waits for user input before closing</param>
  [HideFromIl2Cpp]
  public async Task StopChat(bool waitForInput = false) {
    await writer.Stop(waitForInput);
    Tts.TtsManager.Instance.StopAll();

    IsChatActive = false;
    isInConversation = false;

    // Reset bot emotes just in case
    ChatExecutor.ResetBotEmotes();
  }

  /// <summary>
  /// Resets the chat history and AI state
  /// </summary>
  public void ResetChat() {
    aiAdapter.ResetChat();
    SaveStorage.Instance.RemoveValue("chat-history");
  }

  /// <summary>
  /// Rebuilds the AI provider from the current config and restores persisted history into it.
  /// Called after the provider is changed at runtime (first-run GUI) so the choice takes effect
  /// without restarting the game.
  /// </summary>
  [HideFromIl2Cpp]
  public void RebuildAdapter() {
    aiAdapter = new AiAdapter();
    ReloadHistoryFromSave();
  }

  /// <summary>
  /// Adds a message to the game's narrative log
  /// </summary>
  /// <param name="characterId">ID of the speaking character</param>
  /// <param name="text">Message text</param>
  public void AddToNarrativeLog(string characterId, string text) {
    if (narrativeLog != null) {
      var character = ConversationSingleton.Instance.GetCharacter(characterId);
      narrativeLog.AddLine(character, text);
    }
  }

  /// <summary>
  /// Switches the game to ADV (Story) mode with a bottom dialogue window.
  /// </summary>
  private static void EnterStoryState() {
    var gameScript = GameScript.Instance;
    var live2DController = Live2DControllerSingleton.Instance;
    var currentController = live2DController.GetReadyController("bot");
    var currentBrain = currentController.CurrentBrain;

    currentController.PrepareForDialogue();
    gameScript.ChangeToStoryState();
    currentBrain.EnableBrain();
    currentBrain.ChangeState(new StoryBrainState());
    currentController.SetEnabled(true);
  }

  private static void ExitStoryState() {
    InteractState.OnStoryStateFinished();
  }

  /// <summary>
  /// Main chat loop coroutine that handles the conversation flow
  /// </summary>
  /// <remarks>
  /// Manages the input popup display and waits for user responses.
  /// Runs until IsChatActive becomes false
  /// </remarks>
  [HideFromIl2Cpp]
  private IEnumerator ChatLoop() {
    var uiOverlay = UiOverlay.Instance;
    var gameScript = GameScript.Instance;
    var gameVariables = gameScript.GameVariables;

    narrativeLog = FindObjectOfType<Il2CppFungus.NarrativeLog>();
    IsChatActive = true;

    EnterStoryState();

    // Provider preflight must happen BEFORE warmup (e.g. Ollama model download
    // prompt, Jun webapp login + server-side history pull).
    // If preflight fails or is rejected, we still open the cha
    var preflightTask = aiAdapter.EnsureReadyForChat();
    while (!preflightTask.IsCompleted) {
      yield return null;
    }

    // Warm up the AI provider as soon as user clicks the chat button
    aiAdapter.WarmUp();

    while (IsChatActive) {
      isInConversation = true;
      chatTitle = $"Say something to {gameVariables.botName}";

      // Show the input popup, then strip the paste button / commands text and swap OK for Send + Close.
      var popup = uiOverlay.InputPopup(chatTitle, chatDescription, processUserInput);
      ConfigureChatPopup(popup);

      while (isInConversation) {
        // Wait for bot to finish speaking
        yield return null;
      }
    }

    ExitStoryState();
  }

  private static bool ValidateUserInput(string userInput) {
    return userInput.Trim() != "";
  }

  private async void ProcessUserInput(string userInput) {
    try {
      // The actual logic is moved to a separate method so that
      // the code in the finally block is executed anyway
      await ProcessUserInputInternal(userInput);
    } finally {
      // Add a small delay so the window has time to close
      await Task.Delay(500);
      isInConversation = false;
    }
  }

  [HideFromIl2Cpp]
  private async Task ProcessUserInputInternal(string userInput) {
    if (userInput.StartsWith("/")) {
      switch (userInput.ToLower()) {
        case "/exit":
          await StopChat();
          return;
        case "/reset":
          ResetChat();
          return;
        case "/clear":
          ChatExecutor.ResetBotEmotes();
          return;
        case "/pack":
          // TODO: Implement history packing
          return;
      }
    }

    if (!ValidateUserInput(userInput)) {
      return;
    }

    // Start fetching AI response in background immediately while player reads and clicks their echo.
    // This hides VRAM/loading/network latency completely.
    var chunksQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
    var fetchCompletion = new TaskCompletionSource<bool>();
    _ = Task.Run(async () => {
      try {
        await foreach (var chunk in aiAdapter.SendMessage(userInput)) {
          chunksQueue.Enqueue(chunk);
        }
        fetchCompletion.TrySetResult(true);
      } catch (Exception ex) {
        fetchCompletion.TrySetException(ex);
      }
    });

    // Echo the player's line in the dialogue box, the same way the bot speaks it.
    await SayPlayerLine(userInput);

    var tts = Tts.TtsManager.Instance;
    bool prepared = false;

    tts.BeginUtterance();
    try {
      await parser.Prepare();
      prepared = true;

      // Stream the chunks as they arrive from the background thread
      while (!fetchCompletion.Task.IsCompleted || !chunksQueue.IsEmpty) {
        if (chunksQueue.TryDequeue(out var chunk)) {
          await parser.Parse(chunk);
        } else {
          // Wait briefly for next chunk
          await Task.Delay(10);
        }
      }

      // Propagate any exceptions from the background fetch
      if (fetchCompletion.Task.IsFaulted) {
        throw fetchCompletion.Task.Exception.InnerException ?? fetchCompletion.Task.Exception;
      }

      // Speak the trailing partial sentence before waiting for the user's click
      tts.CompleteUtterance();
      await parser.Flush();

      PersistHistoryToSave();
    } catch (Exception ex) {
      logger.LogError($"Error during AI response: {ex.Message}");
      tts.CompleteUtterance();

      if (prepared) {
        // Flush writes any partial text to the dialog and unlocks the Writer.
        // Without this the Writer stays locked and the game softlocks.
        try { await parser.Flush(); } catch { /* best effort */ }
      }
    }
  }

  /// <summary>
  /// Reworks the native chat input popup: removes the "Paste from clipboard" button and the
  /// commands text, and replaces the single OK with a green Send and a red Close button.
  /// </summary>
  [HideFromIl2Cpp]
  private void ConfigureChatPopup(InputPopup popup) {
    if (popup == null) {
      return;
    }
    chatPopup = popup;
    try {
      // Re-open the popup through the game's own choice path so the buttons keep their built-in
      // close-on-click behavior (ButtonList.InitializeFromPopupChoices did NOT, which softlocked
      // the modal). PopupStringChoice.Action receives the typed text. Empty body drops the
      // commands box.
      var choices = new Il2CppSystem.Collections.Generic.List<PopupStringChoice>();
      var send = new PopupStringChoice { Text = "Send", Action = new Action<string>(t => ProcessUserInput(t ?? "")) };
      var close = new PopupStringChoice { Text = "Close", Action = new Action<string>(_ => CloseFromPopup()) };
      choices.Add(send);
      choices.Add(close);
      popup.Open(choices, chatTitle, "", "", Il2CppTMPro.TMP_InputField.ContentType.Standard);

      // Every Open pushes the popup onto UIManager.currentlyOpenPopups (no duplicate check),
      // so this second Open leaves the same popup on the stack twice. SyncAllPopups walks the
      // stack top-down and only the first entry keeps its CanvasGroup interactable; the
      // duplicate entry immediately switches the same popup back to non-interactable, leaving
      // it visible but deaf to clicks and typing. Drop the duplicate and resync.
      var uiManager = UIManager.Instance;
      if (uiManager != null) {
        uiManager.currentlyOpenPopups.Remove(popup);
        uiManager.SyncAllPopups();
      }

      // Remove the "Paste from clipboard" button and the now-empty commands text box.
      if (popup.pasteFromClipboardButton != null) {
        popup.pasteFromClipboardButton.gameObject.SetActive(false);
      }
      if (popup.textTmp != null) {
        popup.textTmp.gameObject.SetActive(false);
      }
    } catch (Exception ex) {
      logger.LogError($"Failed to configure chat popup: {ex.Message}");
    }
  }

  // Closes the chat entirely (the "Close" button). The native choice already dismisses the popup;
  // if it is somehow still alive, close it through the game's own path so it is also removed
  // from UIManager.currentlyOpenPopups (a raw Destroy would leave a stale stack entry that
  // breaks every popup opened afterwards).
  private async void CloseFromPopup() {
    try {
      if (chatPopup != null) {
        chatPopup.CloseFromUIOverlay();
      }
      await StopChat();
    } catch (Exception ex) {
      logger.LogError($"Failed to close chat: {ex.Message}");
    }
  }

  /// <summary>
  /// Shows the player's own message in the game's dialogue box using the same
  /// conversation path the bot uses for its "..." placeholder. The next
  /// DoConversation call in parser.Prepare() naturally replaces this one.
  /// </summary>
  [HideFromIl2Cpp]
  private async Task SayPlayerLine(string text) {
    var tcs = new TaskCompletionSource();

    Action<Il2CppFungus.SayDialog> handler = null;
    handler = new Action<Il2CppFungus.SayDialog>(dialog => {
      Il2CppFungus.SayDialogSignals.OnDialogFinished -= handler;
      tcs.TrySetResult();
    });
    Il2CppFungus.SayDialogSignals.OnDialogFinished += handler;

    await MainThreadRunner.Run(() => {
      var say = Il2CppFungus.SayDialog.GetSayDialog();
      if (say != null) {
        var speaker = ConversationSingleton.Instance.GetCharacter("You");
        if (speaker != null) {
          say.SetCharacter(speaker);
        }
      }
      ChatWriter.Instance.StartCoroutine(
        BetterConversationManager.DoConversation($"You: {text}")
      );
    });

    // Wait until the user clicks and the player's dialog finishes typing/advancing
    await tcs.Task;
  }
}

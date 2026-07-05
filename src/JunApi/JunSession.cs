using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MdrgAiDialog.Utils;

namespace MdrgAiDialog.JunApi;

/// <summary>
/// Authenticated HTTP session against the Jun webapp API
/// </summary>
/// <remarks>
/// The webapp authenticates with an httponly session cookie (omega_session)
/// obtained from /api/auth.php?action=login. One session is shared by the
/// chat provider and the TTS client so the game shows up as a single logged-in
/// user. TLS is terminated by the webapp's NGINX, so this class is a plain
/// HTTPS client
/// </remarks>
public class JunSession {
  private static readonly Logger logger = new("JunSession");

  private static JunSession instance;

  /// <summary>
  /// Shared session built from the [Jun] config block (created on first use)
  /// </summary>
  public static JunSession Instance {
    get {
      instance ??= new JunSession(ModConfig.GetJunConfig());
      return instance;
    }
  }

  /// <summary>
  /// Drops the cached session so the next <see cref="Instance"/> access rebuilds it from the
  /// current config. Call after the Jun URL/credentials change (e.g. from the settings panel).
  /// </summary>
  public static void Reset() {
    instance = null;
  }

  /// <summary>
  /// The [Jun] configuration this session was created with
  /// </summary>
  public JunConfig Config { get; }

  /// <summary>
  /// HttpClient with the shared cookie container. Base address is {ApiUrl}/
  /// </summary>
  public HttpClient Client { get; }

  private bool loggedIn = false;

  public JunSession(JunConfig config) {
    Config = config;

    var handler = new HttpClientHandler {
      CookieContainer = new CookieContainer(),
      UseCookies = true
    };

    Client = new HttpClient(handler) {
      BaseAddress = new Uri(EnsureTrailingSlash(config.ApiUrl)),
      Timeout = TimeSpan.FromSeconds(Math.Max(30, config.TimeoutSeconds))
    };
  }

  /// <summary>
  /// Makes sure the session cookie is valid, logging in when needed
  /// </summary>
  /// <returns>True when authenticated</returns>
  public async Task<bool> EnsureAuthenticated() {
    if (loggedIn && await CheckSession()) {
      return true;
    }

    return await Login();
  }

  /// <summary>
  /// Marks the session as expired so the next call re-authenticates
  /// (call after receiving a 401 from any endpoint)
  /// </summary>
  public void InvalidateSession() {
    loggedIn = false;
  }

  private async Task<bool> CheckSession() {
    try {
      var response = await Client.GetAsync("api/auth.php?action=me");
      return response.IsSuccessStatusCode;
    } catch (Exception) {
      return false;
    }
  }

  private async Task<bool> Login() {
    if (string.IsNullOrEmpty(Config.Email) || string.IsNullOrEmpty(Config.Password)) {
      logger.LogError("Jun webapp credentials are not set ([Jun] Email/Password in the config)");
      return false;
    }

    try {
      var body = JsonSerializer.Serialize(new { email = Config.Email, password = Config.Password });
      var response = await Client.PostAsync(
        "api/auth.php?action=login",
        new StringContent(body, Encoding.UTF8, "application/json")
      );

      if (!response.IsSuccessStatusCode) {
        logger.LogError($"Jun webapp login failed: {response.StatusCode}");
        loggedIn = false;
        return false;
      }

      loggedIn = true;
      logger.Log("Logged in to the Jun webapp");
      return true;
    } catch (Exception ex) {
      logger.LogError($"Jun webapp login failed: {ex.Message}");
      loggedIn = false;
      return false;
    }
  }

  private static string EnsureTrailingSlash(string url) {
    return url.EndsWith("/") ? url : url + "/";
  }
}

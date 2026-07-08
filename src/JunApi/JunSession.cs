using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MdrgAiDialog.Utils;

namespace MdrgAiDialog.JunApi;

/// <summary>
/// Authenticated HTTP session against the Jun webapp API. Logs in via
/// /api/auth.php and keeps the httponly session cookie (omega_session); one
/// session is shared by the chat provider and the TTS client. TLS is handled
/// by the webapp's NGINX, so this is just a plain HTTPS client.
/// </summary>
public class JunSession {
  private static readonly Logger logger = new("JunSession");

  private static JunSession instance;

  // Shared session built from the [Jun] config block (created on first use)
  public static JunSession Instance {
    get {
      instance ??= new JunSession(ModConfig.GetJunConfig());
      return instance;
    }
  }

  // Drops the cached session so the next Instance rebuilds from current config
  // (call after the Jun URL/credentials change, e.g. from the settings panel)
  public static void Reset() {
    instance?.Client.Dispose();
    instance = null;
  }

  public JunConfig Config { get; }

  // Shares the cookie container; base address is {ApiUrl}/
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

  // Makes sure the session cookie is valid (logging in when needed); true when authenticated
  public async Task<bool> EnsureAuthenticated() {
    if (loggedIn && await CheckSession()) {
      return true;
    }

    return await Login();
  }

  // Marks the session expired so the next call re-authenticates (after a 401)
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

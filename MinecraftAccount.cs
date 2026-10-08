using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HowlingWhispers.CodaLauncher;

internal sealed record DeviceSignIn(string Code, string Url, DateTimeOffset ExpiresAt);
internal sealed record AccountView(bool Configured, bool SignedIn, bool OfflineAvailable,
    string PlayerName, string Uuid, string Status, DateTimeOffset? VerifiedAt, string Storage);
internal sealed record GameIdentity(string PlayerName, string Uuid, string AccessToken, bool Offline, string ClientId);
internal sealed record AccountSession(string PlayerName, string Uuid, string AccessToken,
    string RefreshToken, DateTimeOffset ExpiresAt, DateTimeOffset VerifiedAt);

internal interface IAccountVault
{
    AccountSession? Load();
    void Save(AccountSession session);
    void Delete();
    string Description { get; }
}

// Tokens and the offline ownership record are never written in plaintext.
// Other platforms keep credentials in memory until native keychain integration is added.
internal sealed class AccountVault : IAccountVault
{
    private readonly string _path;
    public AccountVault(string? path = null) => _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HowlingWhispers", "CodaLauncher", "account.dpapi");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CodaLauncher.Account.v1");
    public string Description => OperatingSystem.IsWindows() ? "Protected by your Windows account" : "This launcher session only";
    public AccountSession? Load()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(_path)) return null;
        try
        {
            var data = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<AccountSession>(data); }
            finally { CryptographicOperations.ZeroMemory(data); }
        }
        catch { return null; } // Corrupt or foreign-user caches never grant access.
    }
    public void Save(AccountSession session)
    {
        if (!OperatingSystem.IsWindows()) return;
        var data = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var protectedData = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path + ".tmp", protectedData);
            File.Move(_path + ".tmp", _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public void Delete() { if (File.Exists(_path)) File.Delete(_path); }
}

internal sealed class MinecraftAccount
{
    private const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";
    private const string Scope = "XboxLive.signin offline_access";
    private readonly HttpClient _http;
    private readonly IAccountVault _vault;
    private readonly string _clientId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AccountSession? _session;
    private string _status = "Sign in with Microsoft to verify Minecraft Java ownership.";
    internal MinecraftAccount(HttpClient? http = null, IAccountVault? vault = null, string? clientId = null)
    {
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
        _vault = vault ?? new AccountVault();
        _clientId = clientId ?? ReadClientId();
        _session = _vault.Load();
        if (_session is not null && !ValidProfile(_session.PlayerName, _session.Uuid)) _session = null;
        if (!Configured) _status = "Microsoft sign-in is awaiting CodaLauncher's app registration.";
        else if (_session is not null) _status = "Saved account. Online play will verify ownership again.";
    }
    public bool Configured => Guid.TryParse(_clientId, out var id) && id != Guid.Empty;
    public AccountView View => new(Configured, _session is not null, OfflineAllowed(_session),
        _session?.PlayerName ?? "", _session?.Uuid ?? "", _status, _session?.VerifiedAt, _vault.Description);
    private static bool OfflineAllowed(AccountSession? session) => session is not null &&
        session.VerifiedAt <= DateTimeOffset.UtcNow && session.VerifiedAt > DateTimeOffset.UtcNow.AddDays(-30);
    private static bool ValidProfile(string name, string uuid) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_]{1,16}$") &&
        System.Text.RegularExpressions.Regex.IsMatch(uuid, "^[0-9a-fA-F]{32}$");
    private static string ReadClientId()
    {
        try
        {
            using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "auth", "microsoft.json")));
            return config.RootElement.GetProperty("clientId").GetString() ?? "";
        }
        catch { return ""; }
    }
    public async Task SignInAsync(Action<DeviceSignIn> prompt, CancellationToken ct)
    {
        if (!Configured) throw new InvalidOperationException("Microsoft sign-in needs CodaLauncher's registered application ID.");
        await _gate.WaitAsync(ct);
        try
        {
            _vault.Delete(); _session = null;
            using var device = await FormAsync("devicecode", new() { ["client_id"] = _clientId, ["scope"] = Scope }, ct);
            var root = device.RootElement;
            var code = root.GetProperty("device_code").GetString()!;
            var expires = DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32());
            var interval = Math.Max(1, root.GetProperty("interval").GetInt32());
            // The browser destination is fixed; never open a URL returned by an arbitrary feed.
            prompt(new(root.GetProperty("user_code").GetString()!, "https://microsoft.com/devicelogin", expires));
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(expires - DateTimeOffset.UtcNow);
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), limit.Token);
                using var response = await _http.PostAsync(Authority + "token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = _clientId, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code", ["device_code"] = code
                }), limit.Token);
                using var token = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(limit.Token), cancellationToken: limit.Token);
                if (response.IsSuccessStatusCode)
                {
                    await VerifyMicrosoftTokenAsync(token.RootElement, limit.Token);
                    return;
                }
                var error = token.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                if (error == "authorization_pending") continue;
                if (error == "slow_down") { interval += 5; continue; }
                throw new InvalidOperationException(error is "access_denied" or "authorization_declined"
                    ? "Microsoft sign-in was declined." : "Microsoft sign-in expired or could not complete. Please try again.");
            }
        }
        finally { _gate.Release(); }
    }
    public async Task<GameIdentity> PrepareLaunchAsync(bool offline, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (offline)
            {
                if (!OfflineAllowed(_session)) throw new InvalidOperationException("Sign in and verify ownership before offline play. Offline verification lasts 30 days.");
                return new(_session!.PlayerName, _session.Uuid, "0", true, _clientId);
            }
            if (!Configured || _session is null) throw new InvalidOperationException("Open Profile and sign in with your Minecraft Java account first.");
            try
            {
                using var token = await FormAsync("token", new() { ["client_id"] = _clientId, ["grant_type"] = "refresh_token", ["refresh_token"] = _session.RefreshToken, ["scope"] = Scope }, ct);
                await VerifyMicrosoftTokenAsync(token.RootElement, ct);
            }
            catch (AccountRejectedException) { _session = null; _vault.Delete(); throw; }
            // Network errors do not become successful verification or silently switch to offline.
            return new(_session!.PlayerName, _session.Uuid, _session.AccessToken, false, _clientId);
        }
        finally { _gate.Release(); }
    }
    public async Task SignOutAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { _vault.Delete(); _session = null; _status = "Signed out. Saved worlds are unchanged."; }
        finally { _gate.Release(); }
    }
    private async Task VerifyMicrosoftTokenAsync(JsonElement microsoft, CancellationToken ct)
    {
        var msa = microsoft.GetProperty("access_token").GetString()!;
        var refresh = microsoft.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : _session?.RefreshToken;
        if (string.IsNullOrWhiteSpace(refresh)) throw new InvalidOperationException("Microsoft did not grant a renewable sign-in. Please sign in again.");
        using var xbox = await PostAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msa },
            RelyingParty = "http://auth.xboxlive.com", TokenType = "JWT"
        }, ct);
        using var xsts = await PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { xbox.RootElement.GetProperty("Token").GetString()! } },
            RelyingParty = "rp://api.minecraftservices.com/", TokenType = "JWT"
        }, ct);
        var hash = xsts.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString();
        using var minecraft = await PostAsync("https://api.minecraftservices.com/authentication/login_with_xbox",
            new { identityToken = $"XBL3.0 x={hash};{xsts.RootElement.GetProperty("Token").GetString()}" }, ct);
        var access = minecraft.RootElement.GetProperty("access_token").GetString()!;
        using var entitlements = await GetAsync("https://api.minecraftservices.com/entitlements/mcstore", access, ct);
        if (!entitlements.RootElement.GetProperty("items").EnumerateArray().Any(item =>
            item.TryGetProperty("name", out var name) && name.GetString() is "game_minecraft" or "product_minecraft"))
            throw new AccountRejectedException("This Microsoft account does not have Minecraft Java ownership.");
        using var profile = await GetAsync("https://api.minecraftservices.com/minecraft/profile", access, ct);
        var player = profile.RootElement.GetProperty("name").GetString()!;
        var uuid = profile.RootElement.GetProperty("id").GetString()!;
        if (!ValidProfile(player, uuid)) throw new AccountRejectedException("Minecraft returned an invalid player profile.");
        var now = DateTimeOffset.UtcNow;
        var session = new AccountSession(player, uuid, access, refresh, now.AddSeconds(minecraft.RootElement.GetProperty("expires_in").GetInt32()), now);
        _vault.Save(session); // Only a complete successful chain can mint an offline ownership record.
        _session = session;
        _status = "Minecraft Java ownership verified. Coda has stamped the paperwork.";
    }
    private async Task<JsonDocument> FormAsync(string path, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await _http.PostAsync(Authority + path, new FormUrlEncodedContent(form), ct);
        if (response.StatusCode == HttpStatusCode.BadRequest && path == "token")
            throw new AccountRejectedException("Microsoft sign-in is no longer valid. Please sign in again.");
        return await ReadAsync(response, ct);
    }
    private async Task<JsonDocument> PostAsync(string url, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(url, content, ct);
        return await ReadAsync(response, ct);
    }
    private async Task<JsonDocument> GetAsync(string url, string bearer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        using var response = await _http.SendAsync(request, ct);
        return await ReadAsync(response, ct);
    }
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Do not expose response bodies: authentication failures can contain credentials or identifying details.
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new AccountRejectedException($"Account verification was rejected (HTTP {(int)response.StatusCode}). Check account access and CodaLauncher's app registration.");
            throw new HttpRequestException($"Account service unavailable (HTTP {(int)response.StatusCode}). Retry, or explicitly choose offline mode if previously verified.");
        }
        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
    }
}
internal sealed class AccountRejectedException(string message) : Exception(message);

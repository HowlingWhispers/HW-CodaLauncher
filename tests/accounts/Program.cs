using System.Net;
using System.Text;
using System.Text.Json;
using HowlingWhispers.CodaLauncher;

const string client = "14713b55-f304-4923-8671-e7b40467778e"; // Test fixture only, never used on a real network.
var local = LocalSingleplayer.Identity();
void LocalCheck(bool ok, string reason) { if (!ok) throw new Exception(reason); }
LocalCheck(LocalSingleplayer.Enabled && local.LocalOnly && local.Offline, "Local development mode disabled or not explicitly marked");
LocalCheck(local.PlayerName == "CodaPlayer" && local.Uuid.Length == 32, "Legacy local player identity invalid");
LocalCheck(local == LocalSingleplayer.Identity(), "Local player UUID is not stable across sessions");
LocalCheck(local.AccessToken == "0" && local.ClientId == "", "Local play must never have online credentials");
AccountSession Session(DateTimeOffset? verified = null) => new("CodaOwner", "1234567890abcdef1234567890abcdef", "secret-game-token", "secret-refresh-token", DateTimeOffset.UtcNow.AddHours(1), verified ?? DateTimeOffset.UtcNow.AddMinutes(-1));
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Reject(Func<Task> action, string message) { try { await action(); } catch { return; } throw new Exception(message); }
MinecraftAccount Account(Vault vault, Handler? handler = null, string id = client) => new(new HttpClient(handler ?? new Handler()), vault, id);
var saved = new Vault(Session());
var account = Account(saved);
var offline = await account.PrepareLaunchAsync(true, default);
Check(offline.Uuid == saved.Value!.Uuid && offline.AccessToken == "0" && offline.Offline, "Offline identity changed or contained online credentials");
Check(!JsonSerializer.Serialize(account.View).Contains("secret"), "Credentials leaked to UI");
await Reject(() => Account(new Vault()).PrepareLaunchAsync(true, default), "Unverified offline access granted");
await Reject(() => Account(new Vault(Session(DateTimeOffset.UtcNow.AddDays(-31)))).PrepareLaunchAsync(true, default), "Expired ownership cache allowed");
await Reject(() => Account(new Vault(Session(DateTimeOffset.UtcNow.AddDays(1)))).PrepareLaunchAsync(true, default), "Future ownership cache allowed");
await Reject(() => Account(new Vault(Session() with { Uuid = "forged" })).PrepareLaunchAsync(true, default), "Malformed UUID allowed");
await Reject(() => Account(new Vault(), id: "").SignInAsync(_ => {}, default), "Unregistered app allowed sign-in");
var success = new Handler();
var onlineVault = new Vault(Session());
var onlineAccount = Account(onlineVault, success);
var online = await onlineAccount.PrepareLaunchAsync(false, default);
Check(!online.Offline && online.AccessToken == "fresh-game-token" && online.PlayerName == "CodaOwner", "Verified identity not returned");
Check(success.Calls == 6 && onlineVault.Value!.RefreshToken == "rotated-refresh", "Incomplete chain or refresh token rotation failed");
Check(onlineAccount.View.OfflineAvailable, "Successful ownership verification did not enable offline");
var notOwnerVault = new Vault(Session());
var notOwner = Account(notOwnerVault, new Handler { Owns = false });
await Reject(() => notOwner.PrepareLaunchAsync(false, default), "Nonowner granted online access");
Check(!notOwner.View.SignedIn && !notOwner.View.OfflineAvailable && notOwnerVault.Value is null, "Denied ownership retained authorization");
var revoked = Account(new Vault(Session()), new Handler { Failure = HttpStatusCode.BadRequest });
await Reject(() => revoked.PrepareLaunchAsync(false, default), "Revoked refresh accepted");
Check(!revoked.View.OfflineAvailable, "Revoked refresh retained offline permission");
var unavailable = Account(new Vault(Session()), new Handler { Failure = HttpStatusCode.ServiceUnavailable });
await Reject(() => unavailable.PrepareLaunchAsync(false, default), "Outage silently became online success");
Check(unavailable.View.OfflineAvailable, "Outage destroyed previously verified offline ownership");
await unavailable.SignOutAsync(default);
Check(!unavailable.View.SignedIn && !unavailable.View.OfflineAvailable, "Sign-out failed to revoke local permission");
var vaultPath = Path.Combine(Path.GetTempPath(), "coda-auth-test-" + Guid.NewGuid() + ".cache");
var protectedVault = new AccountVault(vaultPath);
try {
    protectedVault.Save(Session());
    if (OperatingSystem.IsWindows()) {
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(vaultPath)).Contains("secret"), "Plaintext credential cache");
        Check(protectedVault.Load()?.PlayerName == "CodaOwner", "Protected cache round-trip failed");
        File.WriteAllText(vaultPath, "tampered");
        Check(protectedVault.Load() is null, "Tampered cache accepted");
    } else Check(!File.Exists(vaultPath), "Credentials persisted without OS protection");
} finally { protectedVault.Delete(); }
Console.WriteLine("PASS: ownership, stable UUID, token rotation, offline eligibility, denial, revocation, outage, sign-out, cache protection and UI redaction");

sealed class Vault(AccountSession? session = null) : IAccountVault {
    public AccountSession? Value = session;
    public string Description => "test";
    public AccountSession? Load() => Value;
    public void Save(AccountSession value) => Value = value;
    public void Delete() => Value = null;
}
sealed class Handler : HttpMessageHandler {
    public int Calls;
    public bool Owns = true;
    public HttpStatusCode? Failure;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        Calls++;
        if (Failure is not null) return Task.FromResult(new HttpResponseMessage(Failure.Value));
        var path = request.RequestUri!.AbsolutePath;
        var json = path switch {
            "/consumers/oauth2/v2.0/token" => "{\"access_token\":\"msa\",\"refresh_token\":\"rotated-refresh\"}",
            "/user/authenticate" => "{\"Token\":\"xbox\"}",
            "/xsts/authorize" => "{\"Token\":\"xsts\",\"DisplayClaims\":{\"xui\":[{\"uhs\":\"hash\"}]}}",
            "/authentication/login_with_xbox" => "{\"access_token\":\"fresh-game-token\",\"expires_in\":3600}",
            "/entitlements/mcstore" => Owns ? "{\"items\":[{\"name\":\"game_minecraft\"}]}" : "{\"items\":[]}",
            "/minecraft/profile" => "{\"name\":\"CodaOwner\",\"id\":\"1234567890abcdef1234567890abcdef\"}",
            _ => throw new Exception("Unexpected endpoint " + path)
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }
}

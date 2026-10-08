using System.Security.Cryptography;
using System.Text;

namespace HowlingWhispers.CodaLauncher;

/// <summary>
/// Temporary development-only mode until Microsoft application registration.
/// This identity is NOT proof of Minecraft ownership and must never be used
/// to authenticate CML multiplayer, friend worlds, servers, or shared storage.
/// </summary>
internal static class LocalSingleplayer
{
    // Leave the MicrosoftAccount implementation intact for a future release.
    // For now both launchers run unverified local singleplayer only.
    internal const bool Enabled = true;
    internal const string PlayerName = "CodaPlayer";

    internal static GameIdentity Identity()
    {
        // Match CodaLoader <= 0.0.24's historical offline UUID exactly so that
        // upgrading doesn't change the player inventory in existing saves.
        byte[] uuid = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + PlayerName));
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x30);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
        return new GameIdentity(PlayerName, Convert.ToHexString(uuid).ToLowerInvariant(),
            "0", Offline: true, ClientId: "", LocalOnly: true);
    }
}

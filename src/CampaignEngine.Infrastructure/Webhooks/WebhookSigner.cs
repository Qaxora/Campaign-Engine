using System.Security.Cryptography;
using System.Text;

namespace CampaignEngine.Infrastructure.Webhooks;

/// <summary>
/// Signature scheme: <c>X-Campaign-Signature: sha256=hex(HMAC_SHA256(secret, timestamp + "." + body))</c>
/// with <c>X-Campaign-Timestamp</c> as Unix seconds. Receivers should reject old timestamps to stop replays.
/// </summary>
public static class WebhookSigner
{
    public const string SignatureHeader = "X-Campaign-Signature";
    public const string TimestampHeader = "X-Campaign-Timestamp";
    public const string EventHeader = "X-Campaign-Event";
    public const string DeliveryHeader = "X-Campaign-Delivery";

    public static string Sign(string secret, long timestamp, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    public static bool Verify(string secret, long timestamp, string body, string signature) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(secret, timestamp, body)),
            Encoding.ASCII.GetBytes(signature));

    public static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}

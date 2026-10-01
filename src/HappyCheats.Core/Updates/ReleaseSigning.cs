using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HappyCheats.Core.Updates;

public static class ReleaseSigning
{
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            MaxDepth = 16,
            WriteIndented = true
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static byte[] SerializeManifest(ReleaseManifest manifest)
    {
        UpdateValidation.Manifest(manifest);
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        if (payload.Length > 128 * 1024) throw new InvalidDataException("The update manifest is too large.");
        return payload;
    }

    public static byte[] SerializeEnvelope(SignedReleaseEnvelope envelope) => JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);

    public static SignedReleaseEnvelope Sign(ReleaseManifest manifest, string privateKeyPem)
    {
        using var key = ReadKey(privateKeyPem);
        var payload = SerializeManifest(manifest);
        return new SignedReleaseEnvelope
        {
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        };
    }

    public static ReleaseManifest Verify(SignedReleaseEnvelope envelope, string publicKeyPem)
    {
        if (envelope.Payload is null || envelope.Payload.Length > 180_000 ||
            envelope.Signature is null || envelope.Signature.Length > 2_000)
            throw new InvalidDataException("The signed update feed is too large or incomplete.");
        byte[] payload, signature;
        try { payload = Convert.FromBase64String(envelope.Payload); signature = Convert.FromBase64String(envelope.Signature); }
        catch (FormatException ex) { throw new InvalidDataException("The update signature encoding is invalid.", ex); }
        using var key = ReadKey(publicKeyPem);
        if (payload.Length == 0 || payload.Length > 128 * 1024 ||
            !key.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException("The update feed signature is invalid. The release cannot be trusted.");
        RejectDuplicateProperties(payload);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload, JsonOptions)
            ?? throw new InvalidDataException("The update manifest is empty.");
        UpdateValidation.Manifest(manifest);
        return manifest;
    }

    internal static RSA ReadKey(string pem)
    {
        var key = RSA.Create();
        try
        {
            if (string.IsNullOrWhiteSpace(pem)) throw new InvalidDataException("The trusted update signing key is missing.");
            key.ImportFromPem(pem);
            if (key.KeySize < 2048 || key.KeySize > 8192)
                throw new InvalidDataException("The update signing key must use RSA with 2048 to 8192 bits.");
            return key;
        }
        catch (Exception ex)
        {
            key.Dispose();
            if (ex is InvalidDataException) throw;
            throw new InvalidDataException("The update signing key is invalid.", ex);
        }
    }

    internal static void RejectDuplicateProperties(byte[] json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        Check(document.RootElement);
        static void Check(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new InvalidDataException("The update feed contains duplicate JSON properties.");
                    Check(property.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var value in element.EnumerateArray()) Check(value);
        }
    }
}

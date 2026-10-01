using System.Security.Cryptography;

namespace LaundryTrainer;

internal static class SupportedBuild
{
    // Inspected metadata v31 and native countdown logic on 2026-09-12.
    // Keep both hashes: unchanged type names alone cannot prove timer semantics.
    internal const string NativeSha256 = "7CB67E3558EB20B78090BF6A28DE3DCB002EC48970444E40CB6974C83703D7D5";
    internal const string MetadataSha256 = "A3B7237C05C00A3D46713B1C022BD8075B79B84A08C649679EDB5B13F9317761";

    public static void Verify(string directory)
    {
        Check(Path.Combine(directory, "GameAssembly.dll"), NativeSha256);
        Check(Path.Combine(directory, "Laundry Store Simulator_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), MetadataSha256);
    }

    private static void Check(string path, string expected)
    {
        using var input = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        var actual = Convert.ToHexString(algorithm.ComputeHash(input));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"游戏版本未经验证：{Path.GetFileName(path)} SHA256 {actual}。修改功能已停用，请查看 docs/compatibility.md。");
    }
}

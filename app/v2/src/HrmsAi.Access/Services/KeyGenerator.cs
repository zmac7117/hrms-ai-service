using System.Security.Cryptography;
using System.Text;

namespace HrmsAi.Access.Services;

public static class KeyGenerator
{
    public static string NewKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var text = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "hrms_" + text;
    }

    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));
    public static string Hex(byte[] bytes) => Convert.ToHexString(bytes);
    public static string DisplayPrefix(string key) => key[..Math.Min(12, key.Length)];
}

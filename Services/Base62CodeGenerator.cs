using System.Security.Cryptography;
using System.Text;

namespace ShortUrl.Api.Services;

public class Base62CodeGenerator : ICodeGenerator
{
    private const string Base62Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public string GenerateCode(int length = 6)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Code length must be greater than zero.");
        }

        var randomValue = GenerateRandomUInt64();
        var chars = new char[length];
        var index = length;

        do
        {
            index--;
            chars[index] = Base62Chars[(int)(randomValue % (ulong)Base62Chars.Length)];
            randomValue /= (ulong)Base62Chars.Length;
        }
        while (index > 0 && randomValue > 0);

        while (index > 0)
        {
            index--;
            chars[index] = '0';
        }

        return new string(chars);
    }

    private static ulong GenerateRandomUInt64()
    {
        var buffer = new byte[8];
        RandomNumberGenerator.Fill(buffer);
        return BitConverter.ToUInt64(buffer, 0);
    }
}

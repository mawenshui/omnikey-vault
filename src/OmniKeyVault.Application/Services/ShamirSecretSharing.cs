using System.Security.Cryptography;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: Shamir's Secret Sharing implementation for emergency contact recovery.
/// Splits a secret (e.g., master password) into N shares, any K of which can
/// reconstruct the original. Uses GF(256) arithmetic.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public static class ShamirSecretSharing
{
    /// <summary>Splits a secret byte array into n shares, requiring k shares to reconstruct.</summary>
    public static (int Index, byte[] Share)[] Split(byte[] secret, int n, int k)
    {
        if (k > n) throw new ArgumentException("k must be <= n");
        if (k < 2) throw new ArgumentException("k must be >= 2");
        if (n > 255) throw new ArgumentException("n must be <= 255");
        if (secret == null || secret.Length == 0) throw new ArgumentException("secret is empty");

        var shares = new (int Index, byte[] Share)[n];
        for (var i = 0; i < n; i++)
        {
            shares[i] = (i + 1, new byte[secret.Length]);
        }

        // Process each byte independently
        for (var byteIdx = 0; byteIdx < secret.Length; byteIdx++)
        {
            // Generate k-1 random coefficients (a_1 ... a_{k-1}), a_0 = secret byte
            var coeffs = new byte[k];
            coeffs[0] = secret[byteIdx];
            RandomNumberGenerator.Fill(coeffs.AsSpan(1));

            // Evaluate polynomial at x=1, x=2, ..., x=n
            for (var i = 0; i < n; i++)
            {
                var x = (byte)(i + 1);
                shares[i].Share[byteIdx] = EvaluatePolynomial(coeffs, x);
            }
        }

        return shares;
    }

    /// <summary>Reconstructs the secret from k shares.</summary>
    public static byte[] Combine((int Index, byte[] Share)[] shares, int secretLength)
    {
        if (shares.Length < 2) throw new ArgumentException("Need at least 2 shares");

        var result = new byte[secretLength];

        for (var byteIdx = 0; byteIdx < secretLength; byteIdx++)
        {
            // Lagrange interpolation at x=0
            byte sum = 0;
            for (var i = 0; i < shares.Length; i++)
            {
                var xi = (byte)shares[i].Index;
                var yi = shares[i].Share[byteIdx];

                // Compute Lagrange basis polynomial Li(0)
                byte num = 1, den = 1;
                for (var j = 0; j < shares.Length; j++)
                {
                    if (i == j) continue;
                    var xj = (byte)shares[j].Index;
                    num = GfMul(num, xj);       // 0 - xj = -xj = xj in GF(256)
                    den = GfMul(den, (byte)(xi ^ xj)); // xi - xj
                }

                var term = GfMul(yi, GfDiv(num, den));
                sum ^= term;
            }
            result[byteIdx] = sum;
        }

        return result;
    }

    /// <summary>Evaluates a polynomial in GF(256) at the given x value.</summary>
    private static byte EvaluatePolynomial(byte[] coeffs, byte x)
    {
        byte result = 0;
        for (var i = coeffs.Length - 1; i >= 0; i--)
        {
            result = (byte)(GfMul(result, x) ^ coeffs[i]);
        }
        return result;
    }

    // --- GF(256) arithmetic ---

    private static readonly int[] ExpTable = new int[256];
    private static readonly int[] LogTable = new int[256];

    static ShamirSecretSharing()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            ExpTable[i] = x;
            LogTable[x] = i;
            x <<= 1;
            if (x >= 256) x ^= 0x11d; // irreducible polynomial
        }
        ExpTable[255] = ExpTable[0];
    }

    private static byte GfMul(int a, int b)
    {
        if (a == 0 || b == 0) return 0;
        return (byte)ExpTable[(LogTable[a] + LogTable[b]) % 255];
    }

    private static byte GfDiv(int a, int b)
    {
        if (a == 0) return 0;
        if (b == 0) throw new DivideByZeroException("Division by zero in GF(256)");
        return (byte)ExpTable[(LogTable[a] - LogTable[b] + 255) % 255];
    }

    /// <summary>Encodes a share as a hex string for easy storage.</summary>
    public static string ShareToHex(int index, byte[] share)
        => $"{index:X2}{Convert.ToHexString(share)}";

    /// <summary>Decodes a hex string back to a share.</summary>
    public static (int Index, byte[] Share) HexToShare(string hex)
    {
        var index = int.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber);
        var share = Convert.FromHexString(hex[2..]);
        return (index, share);
    }
}

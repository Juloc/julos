using System.Security.Cryptography;
using System.Text;

using JulOS.Application.Secrets;
using JulOS.Infrastructure.Secrets;

using Microsoft.Extensions.Time.Testing;

namespace JulOS.Infrastructure.Tests.Secrets;

[TestClass]
public sealed class AesGcmSecretProtectorTests
{
    [TestMethod]
    public void CiphertextIsAuthenticatedRandomizedAndReversible()
    {
        using var directory = TestKeyDirectory.Create();
        using var keyRing = SecretEncryptionKeyRing.Load("primary", directory.Path);
        var protector = new AesGcmSecretProtector(keyRing);
        var referenceId = Guid.CreateVersion7();
        var plaintext = Encoding.UTF8.GetBytes("test-only-value-that-must-not-appear");

        try
        {
            var first = protector.Protect(
                referenceId,
                SecretOwningScopeType.Package,
                "de.juloc.example",
                "remote.password",
                plaintext);
            var second = protector.Protect(
                referenceId,
                SecretOwningScopeType.Package,
                "de.juloc.example",
                "remote.password",
                plaintext);

            Assert.IsFalse(first.Nonce.SequenceEqual(second.Nonce));
            Assert.IsFalse(first.Ciphertext.SequenceEqual(second.Ciphertext));
            Assert.IsFalse(Contains(first.Ciphertext, plaintext));

            var decrypted = protector.Unprotect(
                referenceId,
                SecretOwningScopeType.Package,
                "de.juloc.example",
                "remote.password",
                first.KeyId,
                first.Nonce,
                first.Ciphertext,
                first.AuthenticationTag);
            try
            {
                CollectionAssert.AreEqual(plaintext, decrypted);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }

            var failure = Assert.ThrowsExactly<SecretReferenceFailureException>(() => protector.Unprotect(
                referenceId,
                SecretOwningScopeType.Package,
                "de.juloc.other",
                "remote.password",
                first.KeyId,
                first.Nonce,
                first.Ciphertext,
                first.AuthenticationTag));
            Assert.AreEqual(SecretReferenceFailureReason.Unavailable, failure.Reason);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    [TestMethod]
    public void LeaseZerosItsBufferWhenDisposedOrExpired()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero));
        var buffer = Encoding.UTF8.GetBytes("test-only-lease");
        using var lease = new SecretLease(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "remote.password",
            buffer,
            clock.GetUtcNow() + TimeSpan.FromMinutes(1),
            clock);

        Assert.AreEqual("test-only-lease", Encoding.UTF8.GetString(lease.Value.Span));
        clock.Advance(TimeSpan.FromMinutes(1));
        var failure = Assert.ThrowsExactly<SecretReferenceFailureException>(() => _ = lease.Value);
        Assert.AreEqual(SecretReferenceFailureReason.LeaseExpired, failure.Reason);
        Assert.IsTrue(buffer.All(value => value == 0));
    }

    private static bool Contains(ReadOnlySpan<byte> value, ReadOnlySpan<byte> expected) =>
        value.IndexOf(expected) >= 0;

    private sealed class TestKeyDirectory : IDisposable
    {
        private TestKeyDirectory(string path)
        {
            this.Path = path;
        }

        internal string Path { get; }

        internal static TestKeyDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "julos-secret-tests",
                Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "primary.key"),
                Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            return new TestKeyDirectory(path);
        }

        public void Dispose()
        {
            Directory.Delete(this.Path, recursive: true);
        }
    }
}

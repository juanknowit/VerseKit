using System.Runtime.InteropServices;
using System.Text;

namespace VerseKit.Core.Services;

/// <summary>
/// macOS Keychain storage for connection secrets.
/// Uses Security.framework P/Invoke — macOS only.
/// </summary>
public sealed class KeychainService : ISecretStore
{
    private const string DefaultService = "com.versekit.connections";

    // Security.framework constants
    private const int ErrSecSuccess = 0;
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, [MarshalAs(UnmanagedType.LPStr)] string serviceName,
        uint accountNameLength, [MarshalAs(UnmanagedType.LPStr)] string accountName,
        uint passwordLength, byte[] passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychain,
        uint serviceNameLength, [MarshalAs(UnmanagedType.LPStr)] string serviceName,
        uint accountNameLength, [MarshalAs(UnmanagedType.LPStr)] string accountName,
        out uint passwordLength, out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyContent(
        IntPtr itemRef,
        IntPtr attrList,
        uint length, byte[] data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern IntPtr CFRelease(IntPtr cf);

    // The Sec* APIs marshal service/account as UTF-8 (LPStr resolves to UTF-8
    // on .NET for macOS) and want the length in BYTES, not UTF-16 chars. Using
    // string.Length breaks any name with non-ASCII characters (e.g. "Norsk Stål").
    private static uint Utf8Len(string s) => (uint)Encoding.UTF8.GetByteCount(s);

    public Task WriteAsync(string account, byte[] secret, CancellationToken ct = default)
    {
        var service = DefaultService;
        var existing = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            Utf8Len(service), service,
            Utf8Len(account), account,
            out _, out var existingData, out var itemRef);

        int status;
        if (existing == ErrSecSuccess)
        {
            // Find allocated a copy of the existing password — free it; we
            // only needed the itemRef to modify the entry in place.
            if (existingData != IntPtr.Zero)
                SecKeychainItemFreeContent(IntPtr.Zero, existingData);

            status = SecKeychainItemModifyContent(itemRef, IntPtr.Zero, (uint)secret.Length, secret);
            CFRelease(itemRef);
        }
        else
        {
            status = SecKeychainAddGenericPassword(
                IntPtr.Zero,
                Utf8Len(service), service,
                Utf8Len(account), account,
                (uint)secret.Length, secret,
                out itemRef);
            if (itemRef != IntPtr.Zero) CFRelease(itemRef);
        }

        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Keychain write failed with status {status}");

        return Task.CompletedTask;
    }

    public Task<byte[]?> ReadAsync(string account, CancellationToken ct = default)
    {
        var service = DefaultService;
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            Utf8Len(service), service,
            Utf8Len(account), account,
            out var length, out var dataPtr,
            out var itemRef);

        if (status == ErrSecItemNotFound) return Task.FromResult<byte[]?>(null);
        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Keychain read failed with status {status}");

        var bytes = new byte[length];
        Marshal.Copy(dataPtr, bytes, 0, (int)length);
        SecKeychainItemFreeContent(IntPtr.Zero, dataPtr);
        if (itemRef != IntPtr.Zero) CFRelease(itemRef);

        return Task.FromResult<byte[]?>(bytes);
    }

    public Task DeleteAsync(string account, CancellationToken ct = default)
    {
        var service = DefaultService;
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero,
            Utf8Len(service), service,
            Utf8Len(account), account,
            out _, out var foundData, out var itemRef);

        if (status == ErrSecItemNotFound) return Task.CompletedTask;
        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Keychain find-for-delete failed with status {status}");

        // Free the password buffer Find allocated; we only delete by itemRef.
        if (foundData != IntPtr.Zero)
            SecKeychainItemFreeContent(IntPtr.Zero, foundData);

        status = SecKeychainItemDelete(itemRef);
        CFRelease(itemRef);

        if (status != ErrSecSuccess)
            throw new InvalidOperationException($"Keychain delete failed with status {status}");

        return Task.CompletedTask;
    }

    public Task WriteStringAsync(string account, string secret, CancellationToken ct = default) =>
        WriteAsync(account, Encoding.UTF8.GetBytes(secret), ct);

    public async Task<string?> ReadStringAsync(string account, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(account, ct);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    // ISecretStore explicit implementation — delegates to the string helpers above
    Task ISecretStore.WriteAsync(string account, string secret, CancellationToken ct) =>
        WriteStringAsync(account, secret, ct);

    Task<string?> ISecretStore.ReadAsync(string account, CancellationToken ct) =>
        ReadStringAsync(account, ct);
}

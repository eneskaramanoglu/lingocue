using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using LingoCue.Core.Security;

namespace LingoCue.Infrastructure.Security;

/// <summary>
/// Generic passwords in the user's login Keychain (service <c>LingoCue</c>, account = secret name),
/// through Security.framework directly: the <c>security</c> CLI would expose the secret in argv.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class MacKeychainStore : ISecretStore
{
    private const string SecurityFramework = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecDuplicateItem = -25299;
    private static readonly byte[] s_service = Encoding.UTF8.GetBytes("LingoCue");

    public bool IsSupported => true;

    public string? Read(string name)
    {
        var account = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)s_service.Length, s_service, (uint)account.Length, account, out var length, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return null;
        }

        Check(status, "read");
        try
        {
            var bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, (int)length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    public void Write(string name, string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var account = Encoding.UTF8.GetBytes(name);
        var password = Encoding.UTF8.GetBytes(secret);
        try
        {
            var status = SecKeychainAddGenericPassword(
                IntPtr.Zero, (uint)s_service.Length, s_service, (uint)account.Length, account, (uint)password.Length, password, out var added);
            if (status == ErrSecDuplicateItem)
            {
                Check(SecKeychainFindGenericPassword(
                    IntPtr.Zero, (uint)s_service.Length, s_service, (uint)account.Length, account, out _, out var oldData, out var existing), "find");
                try
                {
                    _ = SecKeychainItemFreeContent(IntPtr.Zero, oldData);
                    Check(SecKeychainItemModifyAttributesAndData(existing, IntPtr.Zero, (uint)password.Length, password), "update");
                }
                finally
                {
                    CFRelease(existing);
                }

                return;
            }

            Check(status, "write");
            CFRelease(added);
        }
        finally
        {
            Array.Clear(password);
        }
    }

    public void Delete(string name)
    {
        var account = Encoding.UTF8.GetBytes(name);
        var status = SecKeychainFindGenericPassword(
            IntPtr.Zero, (uint)s_service.Length, s_service, (uint)account.Length, account, out _, out var data, out var item);
        if (status == ErrSecItemNotFound)
        {
            return;
        }

        Check(status, "find");
        try
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
            Check(SecKeychainItemDelete(item), "delete");
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string operation)
    {
        if (status != 0)
        {
            throw new SecretStoreException($"Keychain {operation} failed (OSStatus {status}).");
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport(SecurityFramework)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        uint passwordLength, byte[] passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName,
        out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(SecurityFramework)]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
#pragma warning restore SYSLIB1054
}

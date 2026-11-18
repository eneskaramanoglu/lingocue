using LingoCue.Core.Security;

namespace LingoCue.Infrastructure.Security;

/// <summary>Picks the OS store; on unsupported platforms only environment variables work.</summary>
public static class SecretStoreFactory
{
    public static ISecretStore Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacKeychainStore();
        }

        return new UnsupportedSecretStore();
    }
}

internal sealed class UnsupportedSecretStore : ISecretStore
{
    public bool IsSupported => false;

    public string? Read(string name) => null;

    public void Write(string name, string secret)
        => throw new SecretStoreException("No secure secret store on this platform; use an environment variable.");

    public void Delete(string name)
    {
    }
}

/// <summary>Reads the Gemini key from the environment first (tests/CI), then from the OS store.</summary>
public sealed class ApiKeyProvider(ISecretStore store)
{
    public bool HasGeminiKey => !string.IsNullOrWhiteSpace(GetGeminiKey());

    public static bool GeminiKeyFromEnvironment
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretNames.GeminiApiKeyEnvironment));

    public string? GetGeminiKey()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(SecretNames.GeminiApiKeyEnvironment);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        try
        {
            return store.Read(SecretNames.GeminiApiKey);
        }
        catch (SecretStoreException)
        {
            return null;
        }
    }

    public void SetGeminiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            store.Delete(SecretNames.GeminiApiKey);
        }
        else
        {
            store.Write(SecretNames.GeminiApiKey, key.Trim());
        }
    }
}

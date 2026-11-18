namespace LingoCue.Core.Security;

/// <summary>Well-known secret names.</summary>
public static class SecretNames
{
    public const string GeminiApiKey = "gemini-api-key";

    /// <summary>Environment variable that overrides the stored Gemini key (tests, CI, power users).</summary>
    public const string GeminiApiKeyEnvironment = "GEMINI_API_KEY";
}

/// <summary>
/// OS-protected storage for API keys: Windows Credential Manager, macOS Keychain.
/// Secrets never go to settings files, logs or the repository.
/// </summary>
public interface ISecretStore
{
    /// <summary>False on platforms without a supported store; the app then relies on environment variables.</summary>
    bool IsSupported { get; }

    string? Read(string name);

    void Write(string name, string secret);

    void Delete(string name);
}

public sealed class SecretStoreException : Exception
{
    public SecretStoreException()
    {
    }

    public SecretStoreException(string message)
        : base(message)
    {
    }

    public SecretStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

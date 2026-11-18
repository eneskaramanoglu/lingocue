using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LingoCue.Core.Platform;
using LingoCue.Core.Security;
using LingoCue.Infrastructure.Security;

namespace LingoCue.App.ViewModels;

/// <summary>Stores API keys in the OS secret store. The key text is never logged or shown back.</summary>
public sealed partial class ApiKeysViewModel(ApiKeyProvider keys, ISecretStore store, IPlatformInfo platform) : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string GeminiKeyInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Message { get; private set; }

    public bool IsStoreSupported => store.IsSupported;

    public string StoreName => platform.Platform == OsPlatform.MacOS ? "macOS Anahtar Zinciri (Keychain)" : "Windows Kimlik Bilgisi Yöneticisi";

    public string GeminiStatus => ApiKeyProvider.GeminiKeyFromEnvironment
        ? $"GEMINI_API_KEY ortam değişkeni kullanılıyor (kayıtlı anahtarı geçersiz kılar)."
        : keys.HasGeminiKey ? "Kayıtlı bir anahtar var." : "Anahtar girilmemiş.";

    public bool HasStoredKey => keys.HasGeminiKey && !ApiKeyProvider.GeminiKeyFromEnvironment;

    private bool CanSave => GeminiKeyInput.Trim().Length >= 20;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            keys.SetGeminiKey(GeminiKeyInput);
            GeminiKeyInput = string.Empty;
            Message = $"Anahtar {StoreName} içine kaydedildi.";
        }
        catch (SecretStoreException ex)
        {
            Message = $"Kaydedilemedi: {ex.Message}";
        }

        OnPropertyChanged(nameof(GeminiStatus));
        OnPropertyChanged(nameof(HasStoredKey));
    }

    [RelayCommand]
    private void Remove()
    {
        try
        {
            keys.SetGeminiKey(null);
            Message = "Kayıtlı anahtar silindi.";
        }
        catch (SecretStoreException ex)
        {
            Message = $"Silinemedi: {ex.Message}";
        }

        OnPropertyChanged(nameof(GeminiStatus));
        OnPropertyChanged(nameof(HasStoredKey));
    }
}

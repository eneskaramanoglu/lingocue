using LingoCue.Core.Security;
using LingoCue.Infrastructure.Security;

namespace LingoCue.Core.Tests.Platform;

/// <summary>Round-trips a throwaway secret through the real OS store (Credential Manager / Keychain).</summary>
public class SecretStoreTests
{
    [Fact]
    public void Stores_reads_updates_and_deletes_a_secret()
    {
        var store = SecretStoreFactory.Create();
        if (!store.IsSupported)
        {
            Assert.Skip("No OS secret store on this platform.");
        }

        var name = $"test-{Guid.NewGuid():N}";
        try
        {
            store.Read(name).ShouldBeNull();

            try
            {
                store.Write(name, "ilk-değer-ğüşıöç");
            }
            catch (SecretStoreException ex) when (OperatingSystem.IsMacOS())
            {
                // CI macOS runners may have no unlocked login keychain.
                Assert.Skip($"Keychain unavailable: {ex.Message}");
            }

            store.Read(name).ShouldBe("ilk-değer-ğüşıöç");

            store.Write(name, "ikinci");
            store.Read(name).ShouldBe("ikinci");
        }
        finally
        {
            store.Delete(name);
        }

        store.Read(name).ShouldBeNull();
        store.Delete(name); // deleting twice is fine
    }
}

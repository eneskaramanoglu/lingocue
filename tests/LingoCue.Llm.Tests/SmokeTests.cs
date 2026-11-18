using System.Reflection;

namespace LingoCue.Llm.Tests;

public class SmokeTests
{
    // Placeholder until M4 adds the pipeline and fake IChatClient tests.
    [Fact]
    public void Llm_assembly_loads()
        => Assembly.Load("LingoCue.Llm").GetName().Name.ShouldBe("LingoCue.Llm");
}

using Ralph.Core.Localization;

namespace Ralph.Tests.RunLoop;

public class OutputAdaptersTests
{
    [Fact]
    public void CursorAdapter_ExtractsCurrentTask_AndFinalSuccess_FromCursorJsonl()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.CursorStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);
        var raw = """
                  {"type":"user","message":{"role":"user","content":[{"type":"text","text":"## Current task\nCrie um arquivo de texto com a frase hello otherworld\n\n## Execution mode\nExecute the task now."}]}}
                  {"type":"thinking","subtype":"delta","text":"The user wants me to..."}
                  {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Arquivo `hello_otherworld.txt` criado com a frase \"hello otherworld\"."}]}}
                  {"type":"result","subtype":"success","is_error":false,"result":"Arquivo `hello_otherworld.txt` criado com a frase \"hello otherworld\"."}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);

        Assert.Contains("Current task: Crie um arquivo de texto com a frase hello otherworld", display!);
        Assert.Contains("[Success] - Arquivo `hello_otherworld.txt` criado com a frase \"hello otherworld\".", display!);
        Assert.DoesNotContain("The user wants me to", display!);
    }

    [Fact]
    public void CursorAdapter_UsesAssistantMessage_WhenResultIsMissing()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.CursorStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);

        var raw = """
                  {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Arquivo criado com sucesso."}]}}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);
        Assert.Equal("Arquivo criado com sucesso.", display!.Trim());
    }

    [Fact]
    public void CursorAdapter_SkipsPlanningStyleReasoning_AndKeepsFinalText()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.CursorStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);

        var raw = """
                  {"type":"reasoning","role":"assistant","text":"The user wants me to create a file."}
                  {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"Arquivo criado com sucesso."}]}}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);
        Assert.DoesNotContain("The user wants me to", display!);
        Assert.Contains("Arquivo criado com sucesso.", display!);
    }

    [Fact]
    public void CodexAdapter_ExtractsFinalAssistantMessage_FromJsonEvents()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.CodexStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);

        var raw = """
                  {"type":"thread.started","thread_id":"abc"}
                  {"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"OK"}}
                  {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":2}}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);
        Assert.Equal("OK", display!.Trim());
    }

    [Fact]
    public void CodexAdapter_ExtractsMessage_FromSseDataPrefixedEvents()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.CodexStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);

        var raw = """
                  data: {"type":"item.completed","item":{"id":"i1","type":"agent_message","text":"OK via SSE"}}
                  data: {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":2}}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);
        Assert.Equal("OK via SSE", display!.Trim());
    }

    [Fact]
    public void GeminiAdapter_FallsBackToRaw_WhenNoTextFound()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.GeminiStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);
        var raw = """{"event":"status","state":"running"}""";

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);

        Assert.Equal(raw, display);
    }

    [Fact]
    public void GeminiAdapter_SkipsThinking_AndShowsOnlyUsefulOutput()
    {
        var coreAssembly = typeof(Ralph.Core.RunLoop.RunLoopService).Assembly;
        var adapterType = coreAssembly.GetType("Ralph.Core.RunLoop.OutputAdapters.GeminiStreamJsonOutputAdapter");
        Assert.NotNull(adapterType);
        var adapter = Activator.CreateInstance(adapterType!);
        Assert.NotNull(adapter);
        var method = adapterType!.GetMethod("Adapt");
        Assert.NotNull(method);
        var raw = """
                  {"type":"content","thinking":"chain","text":"internal"}
                  {"type":"content","text":"final answer"}
                  {"usage_metadata":{"input_tokens":10,"output_tokens":20,"total_tokens":30}}
                  """;

        var display = (string?)method!.Invoke(adapter, new object[] { raw, StringCatalog.Default() });
        Assert.NotNull(display);

        Assert.DoesNotContain("chain", display!);
        Assert.Contains("final answer", display!);
        Assert.DoesNotContain("[tokens]", display!);
    }
}

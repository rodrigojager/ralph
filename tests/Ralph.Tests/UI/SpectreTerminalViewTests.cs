using Ralph.UI.Spectre;

namespace Ralph.Tests.UI;

public class SpectreTerminalViewTests
{
    [Fact]
    public void WriteLine_DoesNotThrow_OnRawJsonLikeText()
    {
        var view = new SpectreTerminalView();

        var ex = Record.Exception(() => view.WriteLine("""{"type":"result","items":["a","b"],"ok":true}"""));

        Assert.Null(ex);
    }

    [Fact]
    public void WriteLine_DoesNotThrow_OnBracketedText()
    {
        var view = new SpectreTerminalView();

        var ex = Record.Exception(() => view.WriteLine("[Success] - done"));

        Assert.Null(ex);
    }
}

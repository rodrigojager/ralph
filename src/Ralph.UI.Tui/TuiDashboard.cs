using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Terminal.Gui;
using Ralph.Core.Localization;
using Ralph.Engines.Tokens;

namespace Ralph.UI.Tui;

public sealed class TuiDashboard : IDisposable
{
    private readonly TuiInteraction  _interaction;
    private readonly TuiTerminalView _terminalView;
    private readonly IStringCatalog  _s;
    private Thread?  _uiThread;
    private volatile bool _running;
    private volatile bool _fallbackRequested;
    private volatile bool _healthy;
    private volatile bool _blockingMode;
    private volatile string? _lastErrorMessage;

    // Token state — written by RunLoopService, read by TUI refresh timer
    public TokenUsage? LatestTokenUsage  { get; set; }
    public int         SessionInputTokens  { get; set; }
    public int         SessionOutputTokens { get; set; }

    // When set, the TUI auto-stops once this Task completes.
    public Task<int>? MonitoredTask { get; set; }

    public bool FallbackRequested => _fallbackRequested;

    // IsHealthy is true while either RunBlocking() or Start()'s background thread is live.
    public bool IsHealthy => _healthy && _running && (_uiThread?.IsAlive == true || _blockingMode);
    public string? LastErrorMessage => _lastErrorMessage;

    public TuiDashboard(TuiInteraction interaction, TuiTerminalView terminalView, IStringCatalog? strings = null)
    {
        _interaction  = interaction;
        _terminalView = terminalView;
        _s            = strings ?? StringCatalog.Default();
    }

    // ── Background-thread start (kept for compatibility) ──────────────────────
    public void Start()
    {
        _running  = true;
        _healthy = false;
        _uiThread = new Thread(RunUi) { IsBackground = true, Name = "TUI-Dashboard" };
        _uiThread.Start();
    }

    // ── Main-thread blocking run (preferred — Terminal.Gui works best here) ───
    /// <summary>
    /// Runs Terminal.Gui on the CALLING thread (must be the main thread).
    /// Blocks until the dashboard is stopped — either because MonitoredTask
    /// completed, the user pressed Q/F5, or an error occurred.
    /// </summary>
    public void RunBlocking()
    {
        _running = true;
        _healthy = false;
        _blockingMode = true;
        try
        {
            RunUi();
        }
        finally
        {
            _blockingMode = false;
        }
    }

    public void Stop()
    {
        _running = false;
        _healthy = false;
        _uiThread?.Join(TimeSpan.FromSeconds(3));
    }

    // ── Main UI thread ────────────────────────────────────────────────────────

    private static void WriteDebugLog(string message)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ralph-tui-debug.log");
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch { /* never crash on logging */ }
    }

    private void RunUi()
    {
        try
        {
            WriteDebugLog("Application.Init() starting...");
            Application.Init();
            WriteDebugLog("Application.Init() done. Creating Toplevel explicitly.");

            // Always create the Toplevel explicitly — Application.Top may be null
            // when running on a background thread in some Terminal.Gui v2 environments.
            var top = new Toplevel();
            _healthy = true;
            WriteDebugLog("_healthy = true, building views.");

            // ── Views ─────────────────────────────────────────────────────────

            // Status panel (top-left, 65% width, 8 rows)
            var statusWin  = new Window { Title = _s.Get("tui.panel.status"), X = 0, Y = 0, Width = Dim.Percent(65), Height = 8 };
            var statusLabel = new Label { X = 1, Y = 1, Width = Dim.Fill(), Text = _s.Get("tui.status.starting") };
            statusWin.Add(statusLabel);

            // Tokens panel (top-right)
            var tokensWin  = new Window { Title = _s.Get("tui.panel.tokens"), X = Pos.Right(statusWin), Y = 0, Width = Dim.Fill(), Height = 8 };
            var tokensLabel = new Label
            {
                X = 1,
                Y = 1,
                Text = _s.Format("tui.tokens_template", "-", "-", "-", "-")
            };
            tokensWin.Add(tokensLabel);

            // Activity Log (middle-left, 40% of remaining height)
            var logWin  = new Window { Title = _s.Get("tui.panel.activity_log"), X = 0, Y = Pos.Bottom(statusWin), Width = Dim.Percent(65), Height = Dim.Percent(40) };
            var logList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            ConfigureListScrolling(logList);
            logWin.Add(logList);

            // Progress (middle-right top half)
            var progressWin   = new Window { Title = _s.Get("tui.panel.progress"), X = Pos.Right(logWin), Y = Pos.Bottom(statusWin), Width = Dim.Fill(), Height = Dim.Percent(20) };
            var progressLabel  = new Label { X = 1, Y = 1, Width = Dim.Fill(), Text = "0/0" };
            var progressBar    = new ProgressBar { X = 1, Y = 3, Width = Dim.Percent(90), Height = 1, Fraction = 0f };
            progressWin.Add(progressLabel, progressBar);

            // Errors (middle-right bottom half)
            var errorsWin  = new Window { Title = _s.Get("tui.panel.errors"), X = Pos.Right(logWin), Y = Pos.Bottom(progressWin), Width = Dim.Fill(), Height = Dim.Percent(20) };
            var errorsList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            ConfigureListScrolling(errorsList);
            errorsWin.Add(errorsList);

            // Engine Output (full-width bottom panel, 2 rows reserved for status bar)
            var outputWin  = new Window { Title = _s.Get("tui.panel.engine_output"), X = 0, Y = Pos.Bottom(logWin), Width = Dim.Fill(), Height = Dim.Fill(2) };
            var outputList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(), CanFocus = true };
            ConfigureListScrolling(outputList);
            outputWin.Add(outputList);

            // Status bar with shortcuts
            var statusBar = new StatusBar();
            statusBar.AddShortcutAt(0, new Shortcut(Key.F5, _s.Get("tui.shortcut.fallback"), () =>
            {
                _fallbackRequested = true;
                Application.RequestStop(top);
            }));
            statusBar.AddShortcutAt(1, new Shortcut(Key.Q, _s.Get("tui.shortcut.quit"), () =>
            {
                Application.RequestStop(top);
            }));
            statusBar.AddShortcutAt(2, new Shortcut(Key.PageUp, _s.Get("tui.shortcut.page_up"), () => { }));
            statusBar.AddShortcutAt(3, new Shortcut(Key.PageDown, _s.Get("tui.shortcut.page_down"), () => { }));
            statusBar.AddShortcutAt(4, new Shortcut(Key.Home, _s.Get("tui.shortcut.home"), () => { }));
            statusBar.AddShortcutAt(5, new Shortcut(Key.End, _s.Get("tui.shortcut.end"), () => { }));
            statusBar.AddShortcutAt(6, new Shortcut(Key.C, _s.Get("tui.shortcut.copy"), () => { }));

            top.Add(statusWin, tokensWin, logWin, progressWin, errorsWin, outputWin, statusBar);

            // ── Refresh timer (250 ms) ────────────────────────────────────────
            var logItems    = new ObservableCollection<string>();
            var errorItems  = new ObservableCollection<string>();
            var outputItems = new ObservableCollection<string>();
            var followLog = true;
            var followErrors = true;
            var followOutput = true;
            logList.SetSource(logItems);
            errorsList.SetSource(errorItems);
            outputList.SetSource(outputItems);
            InitializeListViewState(logList);
            InitializeListViewState(errorsList);
            InitializeListViewState(outputList);
            AttachListNavigation(logList, () => logItems.Count, v => followLog = v);
            AttachListNavigation(errorsList, () => errorItems.Count, v => followErrors = v);
            AttachListNavigation(outputList, () => outputItems.Count, v => followOutput = v);

            top.KeyDown += (_, key) =>
            {
                if (key.KeyCode == Key.Q.KeyCode)
                {
                    key.Handled = true;
                    Application.RequestStop(top);
                    return;
                }

                if (key.KeyCode == Key.C.KeyCode)
                {
                    key.Handled = true;
                    var text = BuildFocusedPanelText(top.MostFocused, logList, errorsList, outputList, logItems, errorItems, outputItems);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        _interaction.WriteInfo(_s.Get("tui.copy.empty"));
                        return;
                    }

                    if (TryCopyToSystemClipboard(text))
                    {
                        _interaction.WriteInfo(_s.Get("tui.copy.ok"));
                    }
                    else
                    {
                        var copyPath = SaveCopyFallback(text);
                        _interaction.WriteWarn(_s.Format("tui.copy.fallback_file", copyPath));
                    }
                }
            };

            // Resize detection — poll Console size every tick
            var lastTermW = Console.WindowWidth;
            var lastTermH = Console.WindowHeight;
            var lastStatusText = string.Empty;
            var lastTokensText = string.Empty;
            var lastProgressText = string.Empty;
            var lastProgressFraction = -1f;

            Application.AddTimeout(TimeSpan.FromMilliseconds(250), () =>
            {
                var needsRedraw = false;
                if (!_running)
                {
                    Application.RequestStop(top);
                    return false;
                }

                // Detect terminal resize and force a clean full redraw
                var termW = Console.WindowWidth;
                var termH = Console.WindowHeight;
                if (termW != lastTermW || termH != lastTermH)
                {
                    lastTermW = termW;
                    lastTermH = termH;
                    Application.Driver?.ClearContents();
                    Application.LayoutAndDraw(true);
                    return true;
                }

                // -- Progress
                if (_terminalView.ProgressTotal > 0)
                {
                    var pct    = (float)_terminalView.ProgressCurrent / _terminalView.ProgressTotal;
                    var ptText = $"[{_terminalView.ProgressCurrent}/{_terminalView.ProgressTotal}]" +
                                 (_terminalView.ProgressTask != null ? $" {_terminalView.ProgressTask}" : "");
                    if (!string.Equals(lastProgressText, ptText, StringComparison.Ordinal))
                    {
                        progressLabel.Text = ptText;
                        lastProgressText = ptText;
                        needsRedraw = true;
                    }
                    if (Math.Abs(lastProgressFraction - pct) > 0.0001f)
                    {
                        progressBar.Fraction = pct;
                        lastProgressFraction = pct;
                        needsRedraw = true;
                    }
                }

                // -- Tokens
                var sessIn  = SessionInputTokens;
                var sessOut = SessionOutputTokens;
                var tu      = LatestTokenUsage;
                var ctxText = tu?.ContextUsedPercent.HasValue == true
                    ? $"{tu.ContextUsedPercent.Value:F0}%"
                    : "-";
                var tokensText = _s.Format(
                    "tui.tokens_template",
                    sessIn.ToString(),
                    sessOut.ToString(),
                    (sessIn + sessOut).ToString(),
                    ctxText);
                if (!string.Equals(lastTokensText, tokensText, StringComparison.Ordinal))
                {
                    tokensLabel.Text = tokensText;
                    lastTokensText = tokensText;
                    needsRedraw = true;
                }

                // -- Activity log (drain queue completely each tick)
                var logChanged = false;
                var logBatch = 0;
                while (logBatch < 200 && _interaction.MessageQueue.TryDequeue(out var msg))
                {
                    var ts   = DateTime.Now.ToString("HH:mm");
                    var line = $"[{ts}] {msg.Message}";
                    if (msg.Level == "error") errorItems.Add(line);
                    else                      logItems.Add(line);
                    logChanged = true;
                    logBatch++;
                }
                while (logItems.Count > 500) logItems.RemoveAt(0);
                while (errorItems.Count > 300) errorItems.RemoveAt(0);
                if (logChanged)
                {
                    NormalizeListViewState(logList, logItems.Count, followLog);
                    NormalizeListViewState(errorsList, errorItems.Count, followErrors);
                    needsRedraw = true;
                }

                // -- Engine output
                var outChanged = false;
                var outBatch = 0;
                while (outBatch < 300 && _terminalView.TryDequeueOutput(out var outLine))
                {
                    outputItems.Add(SanitizeListItem(outLine));
                    outChanged = true;
                    outBatch++;
                }
                while (outputItems.Count > 300) outputItems.RemoveAt(0);
                if (outChanged && outputItems.Count > 0)
                {
                    NormalizeListViewState(outputList, outputItems.Count, followOutput);
                    needsRedraw = true;
                }

                // -- Status (after queue drain so final messages are visible)
                if (MonitoredTask?.IsCompleted == true)
                {
                    var completedText = _s.Get("tui.status.completed");
                    if (!string.Equals(lastStatusText, completedText, StringComparison.Ordinal))
                    {
                        statusLabel.Text = completedText;
                        lastStatusText = completedText;
                        needsRedraw = true;
                    }
                }
                else
                {
                    var statusText = _terminalView.CurrentStatus;
                    if (!string.Equals(lastStatusText, statusText, StringComparison.Ordinal))
                    {
                        statusLabel.Text = statusText;
                        lastStatusText = statusText;
                        needsRedraw = true;
                    }
                }

                if (needsRedraw)
                    Application.LayoutAndDraw(true);
                return true; // timer keeps running — user must press Q to exit
            });

            WriteDebugLog("Application.Run() starting.");
            string? lastUiErrorSignature = null;
            var lastUiErrorCount = 0;
            Application.Run(top, ex =>
            {
                var signature = $"{ex.GetType().Name}:{ex.Message}";
                if (string.Equals(signature, lastUiErrorSignature, StringComparison.Ordinal))
                    lastUiErrorCount++;
                else
                {
                    lastUiErrorSignature = signature;
                    lastUiErrorCount = 1;
                }

                // Avoid flooding logs when a render exception loops.
                if (lastUiErrorCount <= 3 || lastUiErrorCount % 100 == 0)
                    WriteDebugLog($"Application.Run() error handler: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

                if (IsListViewStartIndexException(ex))
                {
                    RemoveBlankItems(logItems);
                    RemoveBlankItems(errorItems);
                    RemoveBlankItems(outputItems);
                    NormalizeListViewState(logList, logItems.Count, followLog);
                    NormalizeListViewState(errorsList, errorItems.Count, followErrors);
                    NormalizeListViewState(outputList, outputItems.Count, followOutput);
                }

                _lastErrorMessage = ex.Message;
                return true; // continue — do NOT stop the event loop
            });

            WriteDebugLog("Application.Run() returned normally.");
            Application.Shutdown();
            _healthy = false;
        }
        catch (Exception ex)
        {
            WriteDebugLog($"RunUi() outer catch: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            _fallbackRequested = true;
            _healthy = false;
            _lastErrorMessage = ex.Message;
        }
    }

    public void Dispose() => Stop();

    private static void ConfigureListScrolling(ListView list)
    {
        list.VerticalScrollBar.AutoShow = true;
        list.VerticalScrollBar.Visible = true;
        list.HorizontalScrollBar.AutoShow = false;
        list.HorizontalScrollBar.Visible = false;
    }

    private static void InitializeListViewState(ListView list)
    {
        list.TopItem = 0;
        list.SelectedItem = -1;
    }

    private static void ScrollToLatest(ListView list, int itemCount)
    {
        if (itemCount <= 0)
            return;

        var lastIndex = itemCount - 1;
        list.SelectedItem = lastIndex;
        var viewportHeight = Math.Max(1, list.Viewport.Height);
        list.TopItem = Math.Max(0, itemCount - viewportHeight);
    }

    private static void NormalizeListViewState(ListView list, int itemCount, bool followLatest)
    {
        if (itemCount <= 0)
        {
            list.SelectedItem = -1;
            list.TopItem = 0;
            return;
        }

        var viewportHeight = Math.Max(1, list.Viewport.Height);
        var maxIndex = itemCount - 1;
        var selected = list.SelectedItem;

        if (followLatest)
            selected = maxIndex;
        else
            selected = Math.Clamp(selected, 0, maxIndex);

        var maxTop = Math.Max(0, itemCount - viewportHeight);
        var top = Math.Clamp(list.TopItem, 0, maxTop);

        if (selected < top)
            top = selected;
        else if (selected >= top + viewportHeight)
            top = Math.Max(0, selected - viewportHeight + 1);

        top = Math.Clamp(top, 0, maxTop);
        list.SelectedItem = selected;
        list.TopItem = top;
    }

    private static void AttachListNavigation(
        ListView list,
        Func<int> getItemCount,
        Action<bool> setFollowLatest)
    {
        list.KeyDown += (_, key) =>
        {
            var page = Math.Max(1, list.Viewport.Height - 1);
            var count = getItemCount();
            if (count == 0)
                return;

            var selected = list.SelectedItem;
            var isHome = key.KeyCode == Key.Home.KeyCode;
            var isEnd = key.KeyCode == Key.End.KeyCode;
            var isPageUp = key.KeyCode == Key.PageUp.KeyCode;
            var isPageDown = key.KeyCode == Key.PageDown.KeyCode;
            var isUp = key.KeyCode == Key.CursorUp.KeyCode;
            var isDown = key.KeyCode == Key.CursorDown.KeyCode;

            if (isHome)
            {
                selected = 0;
                setFollowLatest(false);
            }
            else if (isEnd)
            {
                selected = count - 1;
                setFollowLatest(true);
            }
            else if (isPageUp)
            {
                selected = Math.Max(0, selected - page);
                setFollowLatest(false);
            }
            else if (isPageDown)
            {
                selected = Math.Min(count - 1, selected + page);
                setFollowLatest(selected == count - 1);
            }
            else if (isUp)
            {
                selected = Math.Max(0, selected - 1);
                setFollowLatest(false);
            }
            else if (isDown)
            {
                selected = Math.Min(count - 1, selected + 1);
                setFollowLatest(selected == count - 1);
            }
            else
            {
                return;
            }

            list.SelectedItem = selected;
            var viewportHeight = Math.Max(1, list.Viewport.Height);
            list.TopItem = Math.Max(0, Math.Min(selected, count - viewportHeight));
            key.Handled = true;
        };
    }

    private static string SanitizeListItem(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? " " : value;
    }

    private static void RemoveBlankItems(ObservableCollection<string> items)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(items[i]))
                items.RemoveAt(i);
        }
    }

    private static bool IsListViewStartIndexException(Exception ex)
    {
        return ex is ArgumentOutOfRangeException aoo
               && string.Equals(aoo.ParamName, "startIndex", StringComparison.Ordinal);
    }

    private string BuildFocusedPanelText(
        View? focused,
        ListView logList,
        ListView errorsList,
        ListView outputList,
        ObservableCollection<string> logItems,
        ObservableCollection<string> errorItems,
        ObservableCollection<string> outputItems)
    {
        if (focused == null)
            return string.Empty;

        if (ReferenceEquals(focused, logList))
            return BuildPanelText(_s.Get("tui.panel.activity_log"), logItems);
        if (ReferenceEquals(focused, errorsList))
            return BuildPanelText(_s.Get("tui.panel.errors"), errorItems);
        if (ReferenceEquals(focused, outputList))
            return BuildPanelText(_s.Get("tui.panel.engine_output"), outputItems);

        return string.Empty;
    }

    private static string BuildPanelText(string panelTitle, IEnumerable<string> lines)
    {
        var body = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(body) ? string.Empty : $"{panelTitle}{Environment.NewLine}{body}";
    }

    private static bool TryCopyToSystemClipboard(string text)
    {
        try
        {
            ProcessStartInfo psi;
            if (OperatingSystem.IsWindows())
            {
                psi = new ProcessStartInfo("cmd", "/c clip")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi = new ProcessStartInfo("pbcopy")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }
            else
            {
                psi = new ProcessStartInfo("xclip", "-selection clipboard")
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            }

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string SaveCopyFallback(string text)
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), ".ralph");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "tui-copy.txt");
        File.WriteAllText(path, text, Encoding.UTF8);
        return path;
    }
}

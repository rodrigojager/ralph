namespace Ralph.Core.RunLoop;

public sealed class GutterDetector
{
    public int MaxAttemptsWithoutProgress { get; init; } = 3;

    private int _sameTaskAttempts;
    private int? _lastTaskIndex;

    public void RecordAttempt(int taskIndex, bool madeProgress)
    {
        if (_lastTaskIndex != taskIndex)
        {
            _lastTaskIndex = taskIndex;
            _sameTaskAttempts = 0;
        }
        if (madeProgress)
            _sameTaskAttempts = 0;
        else
            _sameTaskAttempts++;
    }

    public bool IsGutter => _sameTaskAttempts >= MaxAttemptsWithoutProgress;

    public void Reset()
    {
        _sameTaskAttempts = 0;
        _lastTaskIndex = null;
    }
}

using System;
using System.IO;
using System.Text;
using BannerlordAI.Core.Llm;
using BannerlordAI.Core.Model;
using BannerlordAI.Core.Orchestration;

namespace BannerlordAI.Module.Logging;

/// <summary>
/// Plain-text developer log, one file per team per battle, under Documents\Mount and Blade II Bannerlord\Logs\AICommander.
/// Warnings are always written; full prompt/response transcripts only when <c>verbose</c>.
/// </summary>
internal sealed class FileBrainLog : IBrainLog
{
    private readonly object _lock = new();
    private readonly string? _path;
    private readonly bool _verbose;

    public FileBrainLog(string label, bool verbose)
    {
        _verbose = verbose;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord", "Logs", "AICommander");
            Directory.CreateDirectory(dir);
            var safe = new StringBuilder();
            foreach (var c in label) safe.Append(char.IsLetterOrDigit(c) ? c : '_');
            _path = Path.Combine(dir, $"battle-{DateTime.Now:yyyyMMdd-HHmmss}-{safe}.log");
        }
        catch
        {
            _path = null;
        }
    }

    public string? FilePath => _path;

    public void Info(string message)
    {
        if (_verbose) Write("INFO", message);
    }

    public void Warn(string message) => Write("WARN", message);

    public void Transcript(Tier tier, LlmRequest request, LlmResult result)
    {
        var head = $"{tier} {request.Model}: {(result.Success ? "ok" : "FAILED " + result.Error)} in {result.LatencySeconds:0.0}s, " +
                   $"tokens in {result.InputTokens} / out {result.OutputTokens} / cache-read {result.CacheReadTokens} / cache-write {result.CacheWriteTokens}, stop {result.StopReason}";
        if (!_verbose)
        {
            if (!result.Success) Write("CALL", head);
            return;
        }
        Write("CALL", head + "\n--- user content ---\n" + request.UserContent + "\n--- reply ---\n" + (result.Text ?? "(none)") + "\n---");
    }

    public void Line(string message) => Write("NOTE", message);

    private void Write(string level, string message)
    {
        if (_path == null) return;
        try
        {
            lock (_lock)
                File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss}] {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never break the battle.
        }
    }
}

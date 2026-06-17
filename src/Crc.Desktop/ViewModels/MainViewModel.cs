using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Crc;

namespace Crc.Desktop.ViewModels;

/// <summary>ViewModel for the main window.</summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private string _targetPath = string.Empty;
    [ObservableProperty] private bool   _isRunning  = false;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool   _legacyMode = false;

    public ObservableCollection<ResultRow> Results { get; } = [];

    // -------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanCompute))]
    private async Task ComputeAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetPath) || !Path.Exists(TargetPath))
        {
            StatusText = "Path does not exist.";
            return;
        }

        IsRunning = true;
        StatusText = "Computing…";
        Results.Clear();

        try
        {
            var options = new CrcOptions();
            IReadOnlyList<FileCrcInfo> results =
                await Task.Run(() => CrcService.Process(TargetPath, options));

            foreach (var r in results)
                Results.Add(new ResultRow(r.RelativePath, r.Crc));

            StatusText = $"Done — {results.Count} file(s).";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private bool CanCompute() => !IsRunning;

    [RelayCommand]
    private async Task SaveAsync(string outputPath)
    {
        if (Results.Count == 0) return;

        var infos = Results.Select(r => new FileCrcInfo
        {
            FileName     = Path.GetFileName(r.Path),
            RelativePath = r.Path,
            Crc          = r.Crc
        }).ToList();

        var opts = new CrcProtocolOptions
        {
            LegacyLineEndings = LegacyMode,
            LegacyEncoding    = LegacyMode,
            LegacyPathCase    = LegacyMode,
        };

        await Task.Run(() => CrcProtocol.Write(infos, outputPath, opts));
        StatusText = $"Saved: {outputPath}";
    }
}

/// <summary>One row in the results grid.</summary>
public sealed record ResultRow(string Path, string Crc);

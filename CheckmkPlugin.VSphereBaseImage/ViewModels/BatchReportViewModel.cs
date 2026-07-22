using System.Text;
using CheckmkPlugin.VSphereBaseImage.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CheckmkPlugin.VSphereBaseImage.ViewModels;

/// <summary>ViewModel des Batch-Ende-Modals. Bereitet Kennzahlen und einen
/// druckbaren Textbericht auf, den der User via Button in die Zwischenablage
/// oder in eine Datei uebertragen kann — Tickets im Fachbereich brauchen den
/// Bericht meist als Prosa.</summary>
public sealed partial class BatchReportViewModel : ObservableObject
{
    public IReadOnlyList<BatchStepResult> Results { get; }
    public string LogText { get; }
    public DateTime FinishedAt { get; } = DateTime.Now;

    public int TotalCount => Results.Count;
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
    public int PublishedCatalogCount => Results.Count(r => r.CatalogPublished is not null);
    public int SnapshotCount => Results.Count(r => r.SnapshotName is not null);

    /// <summary>Kompakter Text fuer die Uebersicht oben im Dialog.</summary>
    public string HeadlineText =>
        $"{SuccessCount}/{TotalCount} erfolgreich, {FailureCount} Fehler, " +
        $"{SnapshotCount} Snapshot(s), {PublishedCatalogCount} Katalog(e) publiziert.";

    public BatchReportViewModel(IReadOnlyList<BatchStepResult> results, string logText)
    {
        Results = results;
        LogText = logText;
    }

    /// <summary>Erzeugt den druckbaren Textbericht — pro VM eine Zeile,
    /// oben Kopfzeile mit Zeitstempel und Aggregat.</summary>
    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Batch-Bericht — {FinishedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine(HeadlineText);
        sb.AppendLine();
        foreach (var r in Results)
        {
            var status = r.Success ? "OK   " : "FEHL ";
            var extras = new List<string>();
            if (r.SnapshotName is not null) extras.Add($"Snapshot={r.SnapshotName}");
            if (r.CatalogPublished is not null) extras.Add($"Katalog={r.CatalogPublished}");
            var suffix = extras.Count > 0 ? "  [" + string.Join(", ", extras) + "]" : "";
            sb.AppendLine($"{status} {r.VmName}: {r.Message}{suffix}");
        }
        return sb.ToString();
    }
}

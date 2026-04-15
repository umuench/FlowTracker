using FlowTracker.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using System.Text;

namespace FlowTracker.Services;

/// <summary>
/// Stellt Exportfunktionen für CSV und PDF auf Basis von Zeitbuchungen bereit.
/// </summary>
public sealed class ReportExportService
{
    /// <summary>
    /// Exportiert Einträge als CSV-Datei inklusive optionaler Summary-Zeile.
    /// </summary>
    public async Task ExportCsvAsync(string filePath, IEnumerable<TimeEntry> entries, ReportSummary? summary = null, CancellationToken cancellationToken = default)
    {
        // EVA:
        // E: Zielpfad, Einträge, optionale Summary.
        // V: Werte serialisieren, CSV escapen, Summary anhängen.
        // A: UTF-8 CSV-Datei auf Dateisystem schreiben.
        var lines = new StringBuilder();
        lines.AppendLine("Id,UserId,StartTimeUtc,EndTimeUtc,DurationHours,Category,Description,CreatedAtUtc,IsDeleted");

        foreach (var entry in entries)
        {
            var start = entry.StartTime.ToUniversalTime().ToString("O");
            var end = entry.EndTime?.ToUniversalTime().ToString("O") ?? string.Empty;
            var created = entry.CreatedAt.ToUniversalTime().ToString("O");
            var durationHours = entry.EndTime is null ? string.Empty : (entry.EndTime.Value - entry.StartTime).TotalHours.ToString("F2");
            lines.AppendLine($"{entry.Id},{Escape(entry.UserId)},{start},{end},{durationHours},{Escape(entry.Category)},{Escape(entry.Description)},{created},{(entry.IsDeleted ? 1 : 0)}");
        }

        if (summary is not null)
        {
            lines.AppendLine();
            lines.AppendLine("Summary");
            lines.AppendLine("Period,EntryCount,ProductiveHours,TargetHours,OvertimeHours,MissingHours");
            lines.AppendLine(
                $"{Escape(summary.PeriodLabel)},{summary.EntryCount},{summary.ProductiveDuration.TotalHours:F2},{summary.TargetDuration.TotalHours:F2},{summary.OvertimeDuration.TotalHours:F2},{summary.MissingDuration.TotalHours:F2}");
        }

        await File.WriteAllTextAsync(filePath, lines.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Exportiert Einträge als PDF-Bericht mit optionaler Summary im Footer.
    /// </summary>
    public Task ExportPdfAsync(string filePath, string title, IEnumerable<TimeEntry> entries, ReportSummary? summary = null, CancellationToken cancellationToken = default)
    {
        // EVA:
        // E: Zielpfad, Titel, Einträge, optionale Summary.
        // V: Dokumentlayout inkl. Tabelle und Footer aufbauen.
        // A: PDF-Datei erzeugen und am Zielpfad ablegen.
        QuestPDF.Settings.License = LicenseType.Community;
        var entryList = entries.ToList();

        Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(20);
                    page.Size(PageSizes.A4);
                    page.Header().Text(title).FontSize(18).SemiBold();
                    page.Content().PaddingVertical(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(42);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(2.4f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Text("ID").SemiBold();
                            header.Cell().Text("Start").SemiBold();
                            header.Cell().Text("Ende").SemiBold();
                            header.Cell().Text("Kategorie").SemiBold();
                            header.Cell().Text("Beschreibung").SemiBold();
                        });

                        foreach (var entry in entryList)
                        {
                            table.Cell().Text(entry.Id.ToString());
                            table.Cell().Text(entry.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
                            table.Cell().Text(entry.EndTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-");
                            table.Cell().Text(entry.Category);
                            table.Cell().Text(string.IsNullOrWhiteSpace(entry.Description) ? "-" : entry.Description);
                        }
                    });

                    page.Footer().AlignRight().Column(footer =>
                    {
                        footer.Item().Text($"{entryList.Count} Einträge").FontSize(10).FontColor(Colors.Grey.Medium);
                        if (summary is not null)
                        {
                            footer.Item().Text(
                                $"Produktiv {summary.ProductiveDuration.TotalHours:F2}h | Soll {summary.TargetDuration.TotalHours:F2}h | +" +
                                $"{summary.OvertimeDuration.TotalHours:F2}h / -{summary.MissingDuration.TotalHours:F2}h")
                                .FontSize(9)
                                .FontColor(Colors.Grey.Medium);
                        }
                    });
                });
            })
            .GeneratePdf(filePath);

        return Task.CompletedTask;
    }

    private static string Escape(string value)
    {
        var escaped = value.Replace("\"", "\"\"");
        return $"\"{escaped}\"";
    }
}

/// <summary>
/// Zusammenfassung für Soll-/Ist-Auswertungen im Dashboard- und Exportkontext.
/// </summary>
public sealed record ReportSummary(
    TimeSpan ProductiveDuration,
    TimeSpan TargetDuration,
    TimeSpan OvertimeDuration,
    TimeSpan MissingDuration,
    int EntryCount,
    string PeriodLabel)
{
    /// <summary>
    /// Leere Summary für Initialzustände ohne Daten.
    /// </summary>
    public static ReportSummary Empty { get; } = new(
        ProductiveDuration: TimeSpan.Zero,
        TargetDuration: TimeSpan.Zero,
        OvertimeDuration: TimeSpan.Zero,
        MissingDuration: TimeSpan.Zero,
        EntryCount: 0,
        PeriodLabel: "-");
}

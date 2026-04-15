using FlowTracker.Domain;
using System.Globalization;

namespace FlowTracker.ViewModels;

public sealed class EditableTimeEntryRow(TimeEntry source) : ViewModelBase
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";

    private string _startLocalText = source.StartTime.ToLocalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);
    private string _endLocalText = source.EndTime?.ToLocalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture) ?? string.Empty;
    private string _category = source.Category;
    private string _description = source.Description;

    public long Id { get; } = source.Id;
    public string UserId { get; } = source.UserId;
    public DateTimeOffset CreatedAt { get; } = source.CreatedAt;
    public bool IsDeleted { get; } = source.IsDeleted;

    public string StartLocalText
    {
        get => _startLocalText;
        set => SetProperty(ref _startLocalText, value);
    }

    public string EndLocalText
    {
        get => _endLocalText;
        set => SetProperty(ref _endLocalText, value);
    }

    public string Category
    {
        get => _category;
        set => SetProperty(ref _category, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool TryBuildTimeEntry(out TimeEntry entry)
    {
        entry = default!;

        if (!DateTime.TryParseExact(StartLocalText, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var startLocal))
        {
            return false;
        }

        DateTimeOffset? endUtc = null;
        if (!string.IsNullOrWhiteSpace(EndLocalText))
        {
            if (!DateTime.TryParseExact(EndLocalText, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var endLocal))
            {
                return false;
            }

            endUtc = new DateTimeOffset(endLocal, TimeZoneInfo.Local.GetUtcOffset(endLocal)).ToUniversalTime();
        }

        var startUtc = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal)).ToUniversalTime();
        entry = new TimeEntry
        {
            Id = Id,
            UserId = UserId,
            StartTime = startUtc,
            EndTime = endUtc,
            Category = Category.Trim(),
            Description = Description.Trim(),
            CreatedAt = CreatedAt,
            IsDeleted = IsDeleted
        };

        return true;
    }
}

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Pandora.Core;

namespace Pandora.App;

public sealed class AgentFeedCardViewModel
{
    public AgentFeedCardViewModel(
        AgentFeedDocument document,
        bool isUnread,
        int openAttentionCount,
        bool isFallback,
        AgentFeedStateDocument state,
        AgentFeedStore store,
        Action<string, string, AgentFeedItemState> itemStateChanged)
    {
        Document = document;
        FeedId = document.FeedId;
        Title = document.Title;
        Summary = string.IsNullOrWhiteSpace(document.Summary) ? "No summary was included." : document.Summary;
        SourceLine = BuildSourceLine(document);
        StatusText = BuildStatusText(document);
        Icon = string.IsNullOrWhiteSpace(document.Icon) ? "\uE9D9" : document.Icon;
        IsUnread = isUnread;
        OpenAttentionCount = openAttentionCount;
        IsFallback = isFallback;
        BadgeText = BuildBadgeText(isUnread, openAttentionCount);

        foreach (var section in document.Sections)
        {
            Sections.Add(new AgentFeedSectionViewModel(section, document.FeedId, state, store, itemStateChanged));
        }

        if (Sections.Count == 0 && !string.IsNullOrWhiteSpace(document.Markdown))
        {
            Sections.Add(new AgentFeedSectionViewModel(new AgentFeedSection
            {
                Id = "markdown",
                Title = "Full Brief",
                Kind = AgentFeedSectionKind.Markdown,
                Text = document.Markdown
            }, document.FeedId, state, store, itemStateChanged));
        }
    }

    public AgentFeedDocument Document { get; }
    public string FeedId { get; }
    public string Title { get; }
    public string Summary { get; }
    public string SourceLine { get; }
    public string StatusText { get; }
    public string Icon { get; }
    public bool IsUnread { get; }
    public int OpenAttentionCount { get; }
    public bool IsFallback { get; }
    public string BadgeText { get; }
    public ObservableCollection<AgentFeedSectionViewModel> Sections { get; } = [];

    public override string ToString()
    {
        return Title;
    }

    private static string BuildSourceLine(AgentFeedDocument document)
    {
        var agent = string.IsNullOrWhiteSpace(document.SourceAgent) ? "local agent" : document.SourceAgent;
        return $"{agent} updated {document.UpdatedUtc.ToLocalTime():g}";
    }

    private static string BuildStatusText(AgentFeedDocument document)
    {
        if (document.ExpiresUtc is not null && document.ExpiresUtc.Value < DateTime.UtcNow)
        {
            return "Stale";
        }

        return document.Status switch
        {
            AgentFeedStatus.ActionNeeded => "Action needed",
            AgentFeedStatus.Attention => "Attention",
            AgentFeedStatus.Error => "Error",
            _ => "Quiet"
        };
    }

    private static string BuildBadgeText(bool isUnread, int openAttentionCount)
    {
        if (isUnread && openAttentionCount > 0)
        {
            return $"new {openAttentionCount}";
        }

        if (isUnread)
        {
            return "new";
        }

        return openAttentionCount > 0 ? openAttentionCount.ToString() : string.Empty;
    }
}

public sealed class AgentFeedSectionViewModel
{
    public AgentFeedSectionViewModel(
        AgentFeedSection section,
        string feedId,
        AgentFeedStateDocument state,
        AgentFeedStore store,
        Action<string, string, AgentFeedItemState> itemStateChanged)
    {
        Title = string.IsNullOrWhiteSpace(section.Title) ? section.Kind.ToString() : section.Title;
        Kind = section.Kind;
        Text = section.Text;

        foreach (var item in section.Items)
        {
            if (section.Kind == AgentFeedSectionKind.Checklist)
            {
                ChecklistItems.Add(new AgentFeedChecklistItemViewModel(item, store.GetEffectiveItemState(state, feedId, item), nextState =>
                    itemStateChanged(feedId, item.Id, nextState)));
            }
            else
            {
                Items.Add(new AgentFeedTextItemViewModel(item));
            }
        }
    }

    public string Title { get; }
    public AgentFeedSectionKind Kind { get; }
    public string Text { get; }
    public ObservableCollection<AgentFeedChecklistItemViewModel> ChecklistItems { get; } = [];
    public ObservableCollection<AgentFeedTextItemViewModel> Items { get; } = [];
}

public sealed class AgentFeedChecklistItemViewModel : INotifyPropertyChanged
{
    private readonly Action<AgentFeedItemState> _stateChanged;
    private AgentFeedItemState _state;

    public AgentFeedChecklistItemViewModel(AgentFeedItem item, AgentFeedItemState state, Action<AgentFeedItemState> stateChanged)
    {
        Id = item.Id;
        Text = item.Text;
        Detail = item.Detail;
        Priority = item.Priority.ToString();
        Source = item.Source;
        _state = state;
        _stateChanged = stateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }
    public string Text { get; }
    public string Detail { get; }
    public string Priority { get; }
    public string Source { get; }

    public bool IsChecked
    {
        get => _state == AgentFeedItemState.Done;
        set
        {
            var next = value ? AgentFeedItemState.Done : AgentFeedItemState.Open;
            if (_state == next)
            {
                return;
            }

            _state = next;
            _stateChanged(next);
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AgentFeedTextItemViewModel
{
    public AgentFeedTextItemViewModel(AgentFeedItem item)
    {
        Text = item.Text;
        Detail = item.Detail;
        Priority = item.Priority.ToString();
        Source = item.Source;
    }

    public string Text { get; }
    public string Detail { get; }
    public string Priority { get; }
    public string Source { get; }
}

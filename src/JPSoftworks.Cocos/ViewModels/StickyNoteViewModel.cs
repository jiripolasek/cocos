using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using JPSoftworks.Cocos.Services.Companion;

namespace JPSoftworks.Cocos.ViewModels;

internal sealed partial class StickyNoteViewModel : ObservableObject
{
    private string _emoji = "😊";
    private string _title = "Companion";
    private bool _isFollowing = true;
    private bool _isSending;
    private bool _isChatSaved;
    private ChatModelOption? _selectedModel;
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public ObservableCollection<NoteItemViewModel> Notes { get; } = new();
    public ObservableCollection<ChatModelOption> ModelOptions { get; } = new();
    public ObservableCollection<ContextItemViewModel> ContextItems { get; } = new();

    public StickyNoteViewModel()
    {
        this.ContextItems.CollectionChanged += (_, _) => this.OnPropertyChanged(nameof(this.HasContext));
    }

    public string Emoji
    {
        get => this._emoji;
        set => this.SetProperty(ref this._emoji, value);
    }

    public string Title
    {
        get => this._title;
        set => this.SetProperty(ref this._title, value);
    }

    public bool IsFollowing
    {
        get => this._isFollowing;
        set
        {
            if (this.SetProperty(ref this._isFollowing, value))
            {
                this.OnPropertyChanged(nameof(this.FollowLabel));
            }
        }
    }

    public string FollowLabel => this.IsFollowing ? "Following" : "Custom position";

    public bool IsSending
    {
        get => this._isSending;
        set => this.SetProperty(ref this._isSending, value);
    }

    public bool IsChatSaved
    {
        get => this._isChatSaved;
        set => this.SetProperty(ref this._isChatSaved, value);
    }

    public ChatModelOption? SelectedModel
    {
        get => this._selectedModel;
        set => this.SetProperty(ref this._selectedModel, value);
    }

    public bool HasContext => this.ContextItems.Count > 0;
}

public sealed class ChatMessage
{
    public required string Text { get; init; }

    public bool IsUser { get; init; }

    public bool IsSystem => !this.IsUser;
}

public sealed class ChatModelOption
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string DisplayName { get; init; }

    public bool IsDefault { get; init; }
}

internal sealed record ContextItemViewModel(string Label, string Content);

internal sealed partial class NoteItemViewModel : ObservableObject
{
    private string _content;
    private bool _isPinned;
    private bool _isFavorite;
    private bool _isFlagged;

    public NoteItemViewModel(CompanionNote note)
    {
        this.Id = note.Id;
        this._content = note.Content;
        this._isPinned = note.IsPinned;
        this._isFavorite = note.IsFavorite;
        this._isFlagged = note.IsFlagged;
        this.CreatedAt = note.CreatedAt;
    }

    public long Id { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Content
    {
        get => this._content;
        set => this.SetProperty(ref this._content, value);
    }

    public bool IsPinned
    {
        get => this._isPinned;
        set => this.SetProperty(ref this._isPinned, value);
    }

    public bool IsFavorite
    {
        get => this._isFavorite;
        set => this.SetProperty(ref this._isFavorite, value);
    }

    public bool IsFlagged
    {
        get => this._isFlagged;
        set => this.SetProperty(ref this._isFlagged, value);
    }
}

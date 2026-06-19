using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Messages.ViewModels;

public enum MessageMode { Offset, Tail }

public class MessagesViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly IMessageService _messageService;
    private IDisposable? _tailSubscription;
    private bool _isPaused;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _selectedMessageValue;
    private string _topicName = "";
    private int _partition;
    private long _startOffset;
    private int _fetchCount = 50;
    private MessageMode _mode = MessageMode.Offset;
    private string _clientFilter = "";
    private KafkaMessage? _selectedMessage;

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "messages";

    public ObservableCollection<KafkaMessage> Messages { get; } = new();
    public ICommand FetchCommand { get; }
    public ICommand StartTailCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopTailCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public IEnumerable<MessageMode> Modes => Enum.GetValues<MessageMode>();

    public string TopicName { get => _topicName; set => this.RaiseAndSetIfChanged(ref _topicName, value); }
    public int Partition { get => _partition; set => this.RaiseAndSetIfChanged(ref _partition, value); }
    public long StartOffset { get => _startOffset; set => this.RaiseAndSetIfChanged(ref _startOffset, value); }
    public int FetchCount { get => _fetchCount; set => this.RaiseAndSetIfChanged(ref _fetchCount, value); }
    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public bool IsPaused { get => _isPaused; private set => this.RaiseAndSetIfChanged(ref _isPaused, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }
    public string? SelectedMessageValue { get => _selectedMessageValue; private set => this.RaiseAndSetIfChanged(ref _selectedMessageValue, value); }

    public MessageMode Mode
    {
        get => _mode;
        set
        {
            this.RaiseAndSetIfChanged(ref _mode, value);
            this.RaisePropertyChanged(nameof(IsOffsetMode));
            this.RaisePropertyChanged(nameof(IsTailMode));
        }
    }

    public bool IsOffsetMode => Mode == MessageMode.Offset;
    public bool IsTailMode => Mode == MessageMode.Tail;

    public KafkaMessage? SelectedMessage
    {
        get => _selectedMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMessage, value);
            SelectedMessageValue = FormatValue(value?.Value);
        }
    }

    public string ClientFilter
    {
        get => _clientFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _clientFilter, value);
            this.RaisePropertyChanged(nameof(FilteredMessages));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public IEnumerable<KafkaMessage> FilteredMessages =>
        string.IsNullOrWhiteSpace(_clientFilter)
            ? Messages
            : Messages.Where(m =>
                (m.Value?.Contains(_clientFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (m.Key?.Contains(_clientFilter, StringComparison.OrdinalIgnoreCase) ?? false));

    public string StatusText =>
        $"{_session.ProfileName}  ·  {Messages.Count} messages" +
        (string.IsNullOrWhiteSpace(_clientFilter) ? "" : " (filtered)");

    private static string? FormatValue(string? raw)
    {
        if (raw == null) return null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { return raw; }
    }

    public MessagesViewModel(IScreen hostScreen, IKafkaSession session, IMessageService messageService)
    {
        HostScreen = hostScreen;
        _session = session;
        _messageService = messageService;

        FetchCommand = ReactiveCommand.CreateFromTask(FetchMessagesAsync);
        StartTailCommand = ReactiveCommand.Create(StartTail);
        PauseCommand = ReactiveCommand.Create(PauseTail);
        StopTailCommand = ReactiveCommand.Create(StopTail);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        Messages.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));
    }

    public async Task FetchMessagesAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var results = await _messageService.FetchMessagesAsync(
                _session, _topicName, _partition, _startOffset, _fetchCount);
            Messages.Clear();
            foreach (var m in results) Messages.Add(m);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void StartTail()
    {
        StopTail();
        IsPaused = false;
        ErrorMessage = null;
        _tailSubscription = _messageService
            .Tail(_session, _topicName)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(
                m => { if (!_isPaused) { Messages.Insert(0, m); this.RaisePropertyChanged(nameof(FilteredMessages)); } },
                ex => ErrorMessage = ex.Message);
    }

    public void PauseTail() => IsPaused = true;

    public void StopTail()
    {
        _tailSubscription?.Dispose();
        _tailSubscription = null;
        IsPaused = false;
    }
}

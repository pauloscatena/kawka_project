using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Unit = System.Reactive.Unit;

namespace Skat.KawkaProject.Features.Topics.ViewModels;

public class TopicsViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly ITopicService _topicService;
    private readonly Action<string, int> _onViewPartitionMessages;
    private bool _isBusy;
    private string? _errorMessage;
    private TopicInfo? _selectedTopic;
    private TopicDetail? _selectedTopicDetail;
    private string _filter = "";
    private List<TopicInfo> _allTopics = new();

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "topics";

    public ObservableCollection<TopicInfo> Topics { get; } = new();
    private bool _isCreatingTopic;
    private string _newTopicName = "";
    private int _newTopicPartitions = 1;
    private int _newTopicReplicationFactor = 1;

    public bool IsCreatingTopic { get => _isCreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isCreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotCreatingTopic)); } }
    public bool IsNotCreatingTopic => !_isCreatingTopic;
    public string NewTopicName { get => _newTopicName; set => this.RaiseAndSetIfChanged(ref _newTopicName, value); }
    public int NewTopicPartitions { get => _newTopicPartitions; set => this.RaiseAndSetIfChanged(ref _newTopicPartitions, value); }
    public int NewTopicReplicationFactor { get => _newTopicReplicationFactor; set => this.RaiseAndSetIfChanged(ref _newTopicReplicationFactor, value); }

    public Interaction<string, bool> ConfirmDelete { get; } = new();

    public ICommand LoadCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public ICommand ViewPartitionMessagesCommand { get; }

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }

    public string Filter
    {
        get => _filter;
        set
        {
            this.RaiseAndSetIfChanged(ref _filter, value);
            ApplyFilter();
        }
    }

    public TopicInfo? SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedTopic, value);
            if (value != null) _ = LoadDetailAsync(value.Name);
        }
    }

    public TopicDetail? SelectedTopicDetail
    {
        get => _selectedTopicDetail;
        private set => this.RaiseAndSetIfChanged(ref _selectedTopicDetail, value);
    }

    public string StatusText => string.IsNullOrWhiteSpace(_filter)
        ? $"{_session.ProfileName}  ·  {_allTopics.Count} topics"
        : $"{_session.ProfileName}  ·  {Topics.Count} / {_allTopics.Count} topics";

    public TopicsViewModel(IScreen hostScreen, IKafkaSession session, ITopicService topicService, Action<string, int> onViewPartitionMessages)
    {
        HostScreen = hostScreen;
        _session = session;
        _topicService = topicService;
        _onViewPartitionMessages = onViewPartitionMessages;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadTopicsAsync);
        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(DeleteTopicAsync);
        ShowCreateFormCommand = ReactiveCommand.Create(() => { IsCreatingTopic = true; NewTopicName = ""; NewTopicPartitions = 1; NewTopicReplicationFactor = 1; });
        CancelCreateCommand = ReactiveCommand.Create(() => IsCreatingTopic = false);
        CreateTopicCommand = ReactiveCommand.CreateFromTask(CreateTopicAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);
        ViewPartitionMessagesCommand = ReactiveCommand.Create<int>(ViewPartitionMessages);

        _ = LoadTopicsAsync();
    }

    public void ViewPartitionMessages(int partition)
    {
        if (SelectedTopicDetail == null) return;
        _onViewPartitionMessages(SelectedTopicDetail.Topic.Name, partition);
    }

    public async Task LoadTopicsAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            _allTopics = (await _topicService.ListTopicsAsync(_session)).ToList();
            ApplyFilter();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task DeleteTopicAsync(string topicName)
    {
        bool confirmed;
        try { confirmed = await ConfirmDelete.Handle(topicName); }
        catch (UnhandledInteractionException<string, bool>) { return; }
        if (!confirmed) return;

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.DeleteTopicAsync(_session, topicName);
            _allTopics.RemoveAll(t => t.Name == topicName);
            if (_selectedTopic?.Name == topicName)
            {
                SelectedTopic = null;
                SelectedTopicDetail = null;
            }
            ApplyFilter();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task CreateTopicAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.CreateTopicAsync(_session, _newTopicName, _newTopicPartitions, (short)_newTopicReplicationFactor);
            IsCreatingTopic = false;
            await LoadTopicsAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task LoadDetailAsync(string topicName)
    {
        try { SelectedTopicDetail = await _topicService.GetTopicDetailAsync(_session, topicName); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private void ApplyFilter()
    {
        Topics.Clear();
        var filtered = string.IsNullOrWhiteSpace(_filter)
            ? _allTopics
            : _allTopics.Where(t => t.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        foreach (var t in filtered) Topics.Add(t);
        this.RaisePropertyChanged(nameof(StatusText));
    }
}

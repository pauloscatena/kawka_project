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
    private bool _isExpandingPartitions;
    private int _newPartitionCount = 1;
    private bool _isRecreatingTopic;
    private int _recreatePartitionCount = 1;
    private string _recreateConfirmName = "";

    public bool IsCreatingTopic { get => _isCreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isCreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotCreatingTopic)); } }
    public bool IsNotCreatingTopic => !_isCreatingTopic;
    public string NewTopicName { get => _newTopicName; set => this.RaiseAndSetIfChanged(ref _newTopicName, value); }
    public int NewTopicPartitions { get => _newTopicPartitions; set => this.RaiseAndSetIfChanged(ref _newTopicPartitions, value); }
    public int NewTopicReplicationFactor { get => _newTopicReplicationFactor; set => this.RaiseAndSetIfChanged(ref _newTopicReplicationFactor, value); }
    public bool IsExpandingPartitions { get => _isExpandingPartitions; private set { this.RaiseAndSetIfChanged(ref _isExpandingPartitions, value); this.RaisePropertyChanged(nameof(IsNotExpandingPartitions)); } }
    public bool IsNotExpandingPartitions => !_isExpandingPartitions;
    public int NewPartitionCount { get => _newPartitionCount; set => this.RaiseAndSetIfChanged(ref _newPartitionCount, value); }
    public bool IsRecreatingTopic { get => _isRecreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isRecreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotRecreatingTopic)); } }
    public bool IsNotRecreatingTopic => !_isRecreatingTopic;
    public int RecreatePartitionCount { get => _recreatePartitionCount; set => this.RaiseAndSetIfChanged(ref _recreatePartitionCount, value); }
    public string RecreateConfirmName
    {
        get => _recreateConfirmName;
        set
        {
            this.RaiseAndSetIfChanged(ref _recreateConfirmName, value);
            this.RaisePropertyChanged(nameof(CanConfirmRecreate));
        }
    }
    public bool CanConfirmRecreate => SelectedTopicDetail != null && _recreateConfirmName == SelectedTopicDetail.Topic.Name;

    public Interaction<string, bool> ConfirmDelete { get; } = new();

    public ICommand LoadCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public ICommand ViewPartitionMessagesCommand { get; }
    public ICommand ShowExpandFormCommand { get; }
    public ICommand CancelExpandCommand { get; }
    public ICommand ExpandPartitionsCommand { get; }
    public ICommand ShowRecreateFormCommand { get; }
    public ICommand CancelRecreateCommand { get; }
    public ICommand RecreateTopicCommand { get; }

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
        ShowExpandFormCommand = ReactiveCommand.Create(() =>
        {
            IsExpandingPartitions = true;
            NewPartitionCount = (SelectedTopicDetail?.Partitions.Count ?? 0) + 1;
        });
        CancelExpandCommand = ReactiveCommand.Create(() => IsExpandingPartitions = false);
        ExpandPartitionsCommand = ReactiveCommand.CreateFromTask(ExpandPartitionsAsync);
        ShowRecreateFormCommand = ReactiveCommand.Create(() =>
        {
            IsRecreatingTopic = true;
            RecreateConfirmName = "";
            RecreatePartitionCount = Math.Max(1, (SelectedTopicDetail?.Partitions.Count ?? 1) - 1);
        });
        CancelRecreateCommand = ReactiveCommand.Create(() => IsRecreatingTopic = false);
        RecreateTopicCommand = ReactiveCommand.CreateFromTask(RecreateTopicAsync);

        _ = LoadTopicsAsync();
    }

    public async Task RecreateTopicAsync()
    {
        if (SelectedTopicDetail == null || !CanConfirmRecreate) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;
        if (_recreatePartitionCount < 1 || _recreatePartitionCount >= currentCount)
        {
            ErrorMessage = $"New partition count must be between 1 and {currentCount - 1}.";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        var replicationFactor = SelectedTopicDetail.Topic.ReplicationFactor;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.RecreateTopicWithFewerPartitionsAsync(_session, topicName, _recreatePartitionCount, replicationFactor);
            IsRecreatingTopic = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task ExpandPartitionsAsync()
    {
        if (SelectedTopicDetail == null) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;
        if (_newPartitionCount <= currentCount)
        {
            ErrorMessage = $"New partition count must be greater than the current count ({currentCount}).";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.ExpandPartitionsAsync(_session, topicName, _newPartitionCount);
            IsExpandingPartitions = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
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

using System.Collections.ObjectModel;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Topics.ViewModels;

public class TopicsViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly ITopicService _topicService;
    private bool _isBusy;
    private string? _errorMessage;
    private TopicInfo? _selectedTopic;
    private TopicDetail? _selectedTopicDetail;
    private string _filter = "";
    private List<TopicInfo> _allTopics = new();

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "topics";

    public ObservableCollection<TopicInfo> Topics { get; } = new();
    public ICommand LoadCommand { get; }
    public ICommand CreateTopicCommand { get; }
    public ICommand DeleteTopicCommand { get; }
    public ICommand DismissErrorCommand { get; }

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

    public TopicsViewModel(IScreen hostScreen, IKafkaSession session, ITopicService topicService)
    {
        HostScreen = hostScreen;
        _session = session;
        _topicService = topicService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadTopicsAsync);
        DeleteTopicCommand = ReactiveCommand.CreateFromTask<string>(DeleteTopicAsync);
        CreateTopicCommand = ReactiveCommand.CreateFromTask<(string name, int partitions, short replication)>(
            async args => await CreateTopicAsync(args.name, args.partitions, args.replication));
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        _ = LoadTopicsAsync();
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
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.DeleteTopicAsync(_session, topicName);
            _allTopics.RemoveAll(t => t.Name == topicName);
            ApplyFilter();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task CreateTopicAsync(string name, int partitions, short replication)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.CreateTopicAsync(_session, name, partitions, replication);
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

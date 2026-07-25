using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Exceptions;
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
    private int? _expandToPartitionCount = 1;
    private bool _isRecreatingTopic;
    private int? _recreatePartitionCount = 1;
    private string _recreateConfirmName = "";

    public bool IsCreatingTopic { get => _isCreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isCreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotCreatingTopic)); } }
    public bool IsNotCreatingTopic => !_isCreatingTopic;
    public string NewTopicName { get => _newTopicName; set => this.RaiseAndSetIfChanged(ref _newTopicName, value); }
    public int NewTopicPartitions { get => _newTopicPartitions; set => this.RaiseAndSetIfChanged(ref _newTopicPartitions, value); }
    public int NewTopicReplicationFactor { get => _newTopicReplicationFactor; set => this.RaiseAndSetIfChanged(ref _newTopicReplicationFactor, value); }
    public bool IsExpandingPartitions { get => _isExpandingPartitions; private set { this.RaiseAndSetIfChanged(ref _isExpandingPartitions, value); this.RaisePropertyChanged(nameof(IsNotExpandingPartitions)); } }
    public bool IsNotExpandingPartitions => !_isExpandingPartitions;
    // Nullable because NumericUpDown.Value is decimal?: clearing the box must mean "no value", not
    // silently keep the previous one - which, on a destructive recreate, means running with a count
    // the user never chose and cannot see.
    public int? ExpandToPartitionCount { get => _expandToPartitionCount; set => this.RaiseAndSetIfChanged(ref _expandToPartitionCount, value); }
    public bool IsRecreatingTopic { get => _isRecreatingTopic; private set { this.RaiseAndSetIfChanged(ref _isRecreatingTopic, value); this.RaisePropertyChanged(nameof(IsNotRecreatingTopic)); } }
    public bool IsNotRecreatingTopic => !_isRecreatingTopic;
    /// <summary>Nullable for the same reason as <see cref="ExpandToPartitionCount"/>.</summary>
    public int? RecreatePartitionCount { get => _recreatePartitionCount; set => this.RaiseAndSetIfChanged(ref _recreatePartitionCount, value); }
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

    public bool IsBusy
    {
        get => _isBusy;
        private set { this.RaiseAndSetIfChanged(ref _isBusy, value); this.RaisePropertyChanged(nameof(IsNotBusy)); }
    }

    /// <summary>
    /// Bound to IsEnabled on every mutating control and on the topic list. A recreate can wait up
    /// to 30s for deletion to propagate, and nothing else must be clickable meanwhile: a second
    /// destructive command issued mid-operation can delete the topic the recreate just put back,
    /// and selecting another topic has its selection silently reverted when the operation finishes.
    /// </summary>
    public bool IsNotBusy => !_isBusy;
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
        private set { this.RaiseAndSetIfChanged(ref _selectedTopicDetail, value); this.RaisePropertyChanged(nameof(CanConfirmRecreate)); }
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
            ExpandToPartitionCount = (SelectedTopicDetail?.Partitions.Count ?? 0) + 1;
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

        // Before the range check: with one partition the valid range is empty and the range message
        // would read "between 1 and 0". This is a fact about the topic, not the input.
        if (currentCount <= 1)
        {
            ErrorMessage = $"'{SelectedTopicDetail.Topic.Name}' has a single partition; there is nothing to reduce.";
            return;
        }
        if (_recreatePartitionCount is not int requestedCount)
        {
            ErrorMessage = "Enter the new partition count.";
            return;
        }
        if (requestedCount < 1 || requestedCount >= currentCount)
        {
            ErrorMessage = $"New partition count must be between 1 and {currentCount - 1} " +
                           $"(the topic currently has {currentCount}).";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        var replicationFactor = SelectedTopicDetail.Topic.ReplicationFactor;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.DeleteAndRecreateTopicAsync(_session, topicName, requestedCount, replicationFactor);
            IsRecreatingTopic = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
            ReselectTopicByName(topicName);
        }
        catch (TopicRecreateFailedException ex)
        {
            ErrorMessage = BuildRecreateFailureMessage(ex);
            if (ex.TopicMayBeDeleted) await ResyncAfterPossibleDeletionAsync(topicName);

            // Close the form when the topic may be gone. Leaving it open leaves a primed
            // destructive button beside an already-typed confirmation name, and the next click
            // both re-runs a destructive operation against a topic that may no longer exist and
            // wipes the message that is currently the only record of how it was configured.
            // A refused delete is different: nothing happened, and retrying is reasonable.
            if (ex.TopicMayBeDeleted) IsRecreatingTopic = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Re-points SelectedTopic at the refreshed TopicInfo instance.
    /// </summary>
    /// <remarks>
    /// ApplyFilter clears the ObservableCollection, which makes the ListBox set SelectedIndex to -1
    /// and write null back through the two-way binding. The re-added item does not restore it
    /// either: TopicInfo is a record with value equality, and the partition count just changed, so
    /// TopicInfo("orders", 2, 1) is not equal to TopicInfo("orders", 4, 1).
    ///
    /// Without this the detail panel stays open showing the topic while SelectedTopic is null, and
    /// the delete button - whose CommandParameter reads from the selection - fires with a null name.
    /// Assigning the backing field directly rather than the property avoids re-triggering the
    /// setter's fire-and-forget detail load, which LoadDetailAsync has just done.
    ///
    /// Coverage note: the unit test for this can only reach the STALE-selection variant (no ListBox
    /// to write null back), so the null-write-back path this method exists for is verified only by
    /// a manual smoke test in the running Avalonia UI. If the ListBox's null write-back is deferred
    /// past this call, this would need to re-run after it - not reproducible headless.
    /// </remarks>
    private void ReselectTopicByName(string topicName)
    {
        var refreshed = Topics.FirstOrDefault(t => t.Name == topicName);
        if (refreshed is null) return;

        _selectedTopic = refreshed;
        this.RaisePropertyChanged(nameof(SelectedTopic));
    }

    /// <summary>
    /// After a failure that may have deleted the topic, refresh the list so the UI stops offering
    /// actions on something that no longer exists.
    /// </summary>
    /// <remarks>
    /// The previous code fetched this same list purely to compute a bool and threw the result away,
    /// leaving Delete / Increase / Recreate live on a destroyed topic.
    /// </remarks>
    private async Task ResyncAfterPossibleDeletionAsync(string topicName)
    {
        try
        {
            _allTopics = (await _topicService.ListTopicsAsync(_session)).ToList();
            ApplyFilter();

            if (_allTopics.Any(t => t.Name == topicName))
            {
                // Deletion had not propagated yet. The warning stands, but the topic is still there
                // and the user should not be shown an empty panel as if it had vanished.
                ReselectTopicByName(topicName);
                return;
            }

            _selectedTopic = null;
            this.RaisePropertyChanged(nameof(SelectedTopic));
            SelectedTopicDetail = null;
        }
        catch
        {
            // The refresh failed too - most likely the same outage that broke the recreate. Keep
            // the data-loss message already in ErrorMessage; it is the one that matters.
        }
    }

    /// <summary>
    /// The service decides whether the data is at risk; this only decides how loudly to say it.
    /// </summary>
    /// <remarks>
    /// The previous version asked the cluster whether the topic still existed and warned only when
    /// it was already gone. That reads the wrong signal: Kafka deletion is asynchronous, so in the
    /// likeliest failure — the propagation timeout — the topic is still listed at the moment of
    /// failure. The user was told "timed out", concluded nothing had happened, and the deletion
    /// completed behind them with nothing recreated.
    /// </remarks>
    private static string BuildRecreateFailureMessage(TopicRecreateFailedException ex)
    {
        // Not at risk: the service already explains what happened and that nothing was modified.
        // Adding a scary prefix here would train the user to dismiss the ones that matter.
        if (!ex.TopicMayBeDeleted) return ex.Message;

        var overrides = ex.PreservedConfig.Count > 0
            ? string.Join(", ", ex.PreservedConfig.Select(kv => $"{kv.Key}={kv.Value}"))
            : "none";

        // Everything needed to rebuild it by hand goes in the message: the topic is gone, so
        // neither the topic list nor the detail panel can answer "what was it?" any more.
        //
        // The instruction stays "check it" rather than "recreate it": in one of these cases the
        // service has just reported that something ELSE took the name back, and "recreate it
        // manually" followed literally would mean deleting a topic that is not ours.
        return $"DATA LOSS RISK: {ex.Message} Check '{ex.TopicName}' on your cluster before doing " +
               $"anything else — it had {ex.Attempt.OriginalPartitionCount} partitions, replication " +
               $"factor {ex.Attempt.ReplicationFactor}, config overrides: {overrides}.";
    }

    public async Task ExpandPartitionsAsync()
    {
        if (SelectedTopicDetail == null) return;
        var currentCount = SelectedTopicDetail.Partitions.Count;
        if (_expandToPartitionCount is not int requestedCount)
        {
            ErrorMessage = "Enter the new partition count.";
            return;
        }
        if (requestedCount <= currentCount)
        {
            ErrorMessage = $"New partition count must be greater than the current count ({currentCount}).";
            return;
        }

        var topicName = SelectedTopicDetail.Topic.Name;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _topicService.ExpandPartitionsAsync(_session, topicName, requestedCount);
            IsExpandingPartitions = false;
            await LoadTopicsAsync();
            await LoadDetailAsync(topicName);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public void ViewPartitionMessages(int partitionId)
    {
        if (SelectedTopicDetail == null) return;
        _onViewPartitionMessages(SelectedTopicDetail.Topic.Name, partitionId);
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
        catch (Exception ex)
        {
            // Clear it, do not leave the previously loaded topic on screen. Otherwise the panel and
            // the list disagree about what is selected, and the panel's own actions target two
            // different topics: expand/recreate read SelectedTopicDetail.Topic.Name while the
            // delete button's CommandParameter reads SelectedTopic.Name.
            SelectedTopicDetail = null;
            ErrorMessage = ex.Message;
        }
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

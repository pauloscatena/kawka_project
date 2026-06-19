using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Input;
using ReactiveUI;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Features.Cluster.ViewModels;

public class ClusterViewModel : ReactiveObject, IRoutableViewModel
{
    private readonly IKafkaSession _session;
    private readonly IClusterService _clusterService;
    private bool _isBusy;
    private string? _errorMessage;
    private ConsumerGroupInfo? _selectedGroup;
    private IDisposable? _autoRefresh;

    public IScreen HostScreen { get; }
    public string UrlPathSegment => "cluster";

    public ObservableCollection<BrokerInfo> Brokers { get; } = new();
    public ObservableCollection<ConsumerGroupInfo> ConsumerGroups { get; } = new();
    public ObservableCollection<PartitionLag> Lag { get; } = new();

    public bool IsBusy { get => _isBusy; private set => this.RaiseAndSetIfChanged(ref _isBusy, value); }
    public string? ErrorMessage { get => _errorMessage; private set => this.RaiseAndSetIfChanged(ref _errorMessage, value); }

    public ConsumerGroupInfo? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGroup, value);
            Lag.Clear();
        }
    }

    public string StatusText =>
        $"{_session.ProfileName}  ·  {Brokers.Count} brokers  ·  {ConsumerGroups.Count} groups";

    public ICommand LoadCommand { get; }
    public ICommand LoadLagCommand { get; }
    public ICommand DismissErrorCommand { get; }

    public ClusterViewModel(IScreen hostScreen, IKafkaSession session, IClusterService clusterService)
    {
        HostScreen = hostScreen;
        _session = session;
        _clusterService = clusterService;

        LoadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        LoadLagCommand = ReactiveCommand.CreateFromTask(LoadLagAsync);
        DismissErrorCommand = ReactiveCommand.Create(() => ErrorMessage = null);

        Brokers.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));
        ConsumerGroups.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(StatusText));

        _autoRefresh = Observable.Interval(TimeSpan.FromSeconds(10))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async _ =>
            {
                if (_selectedGroup != null) await LoadLagAsync();
            });

        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var brokers = await _clusterService.ListBrokersAsync(_session);
            var groups = await _clusterService.ListConsumerGroupsAsync(_session);
            Brokers.Clear();
            foreach (var b in brokers) Brokers.Add(b);
            ConsumerGroups.Clear();
            foreach (var g in groups) ConsumerGroups.Add(g);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task LoadLagAsync()
    {
        if (_selectedGroup == null) return;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var lag = await _clusterService.GetGroupLagAsync(_session, _selectedGroup.GroupId);
            Lag.Clear();
            foreach (var l in lag) Lag.Add(l);
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
}

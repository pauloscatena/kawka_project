using Moq;
using Skat.KawkaProject.Core.Interfaces;
using Skat.KawkaProject.Core.Models;
using Skat.KawkaProject.Tui.Commands;
using Skat.KawkaProject.Tui.Safety;

namespace Skat.KawkaProject.Tui.Tests;

public class ConnectionCommandsTests
{
    private sealed class NoConfirmer : IConfirmer
    {
        public Task<bool> ConfirmAsync(DestructiveAction a, CancellationToken ct) => Task.FromResult(false);
    }

    private static CommandContext Ctx(string line, IKafkaSession? session = null) => new()
    {
        Parsed = ArgumentParser.ParseLine(line),
        Session = session,
        Confirmer = new NoConfirmer()
    };

    [Fact]
    public async Task Profiles_lists_saved_profiles_as_a_table()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[]
        {
            new ConnectionProfile { Name = "prod", BootstrapServers = "k1:9092", AuthType = AuthType.SaslSsl }
        });

        var result = await new ProfilesCommand(repo.Object).ExecuteAsync(Ctx("profiles"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        Assert.Single(table.Rows);
        Assert.Equal("prod", table.Rows[0][0]);
        Assert.Contains("k1:9092", table.Rows[0][1]);
    }

    [Fact]
    public async Task Profiles_never_prints_a_password()
    {
        // The repository holds SASL credentials in plain text. A listing that echoed them would put
        // the password into scrollback, into `kawka profiles > file`, and into anyone's screen share.
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[]
        {
            new ConnectionProfile
            {
                Name = "prod", BootstrapServers = "k1:9092", AuthType = AuthType.SaslSsl,
                SaslUsername = "svc-kawka", SaslPassword = "hunter2-should-never-appear"
            }
        });

        var result = await new ProfilesCommand(repo.Object).ExecuteAsync(Ctx("profiles"), CancellationToken.None);

        var table = Assert.IsType<CommandResult.Table>(result);
        var everything = string.Join(" ", table.Rows.SelectMany(r => r).Concat(table.Columns));
        Assert.DoesNotContain("hunter2-should-never-appear", everything);
    }

    [Fact]
    public async Task Profiles_on_an_empty_store_is_an_empty_table_not_an_error()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(Array.Empty<ConnectionProfile>());

        var result = await new ProfilesCommand(repo.Object).ExecuteAsync(Ctx("profiles"), CancellationToken.None);

        Assert.Empty(Assert.IsType<CommandResult.Table>(result).Rows);
    }

    [Fact]
    public async Task Connect_opens_a_session_for_the_named_profile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        var profile = new ConnectionProfile { Name = "prod", BootstrapServers = "k1:9092" };
        repo.Setup(r => r.GetAll()).Returns(new[] { profile });

        var session = new Mock<IKafkaSession>();
        session.Setup(s => s.ProfileName).Returns("prod");
        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(profile)).ReturnsAsync(session.Object);

        var cmd = new ConnectCommand(repo.Object, factory.Object);
        var result = await cmd.ExecuteAsync(Ctx("connect prod"), CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
        Assert.Same(session.Object, cmd.Established);
    }

    [Fact]
    public async Task Connect_without_a_profile_name_is_a_usage_error()
    {
        var cmd = new ConnectCommand(Mock.Of<IConnectionProfileRepository>(), Mock.Of<IKafkaConnectionFactory>());

        var result = await cmd.ExecuteAsync(Ctx("connect"), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Equal(ExitCodes.Usage, failure.ExitCode);
    }

    [Fact]
    public async Task Connect_to_an_unknown_profile_names_the_available_ones()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAll()).Returns(new[] { new ConnectionProfile { Name = "prod" } });

        var cmd = new ConnectCommand(repo.Object, Mock.Of<IKafkaConnectionFactory>());
        var result = await cmd.ExecuteAsync(Ctx("connect staging"), CancellationToken.None);

        var failure = Assert.IsType<CommandResult.Failure>(result);
        Assert.Contains("prod", failure.Message);
    }

    [Fact]
    public async Task A_failed_connect_leaves_no_session_behind_from_an_earlier_one()
    {
        // Established is how the host adopts the new session. If a later failure left the previous
        // value in place, the host would keep handing commands a session the user thinks is gone -
        // and against a profile they did not connect to.
        var repo = new Mock<IConnectionProfileRepository>();
        var good = new ConnectionProfile { Name = "prod", BootstrapServers = "k1:9092" };
        repo.Setup(r => r.GetAll()).Returns(new[] { good });

        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(good)).ReturnsAsync(Mock.Of<IKafkaSession>());

        var cmd = new ConnectCommand(repo.Object, factory.Object);
        await cmd.ExecuteAsync(Ctx("connect prod"), CancellationToken.None);
        Assert.NotNull(cmd.Established);

        await cmd.ExecuteAsync(Ctx("connect nowhere"), CancellationToken.None);

        Assert.Null(cmd.Established);
    }

    [Fact]
    public async Task Connect_matches_a_profile_name_regardless_of_case()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        var profile = new ConnectionProfile { Name = "Prod", BootstrapServers = "k1:9092" };
        repo.Setup(r => r.GetAll()).Returns(new[] { profile });

        var factory = new Mock<IKafkaConnectionFactory>();
        factory.Setup(f => f.ConnectAsync(profile)).ReturnsAsync(Mock.Of<IKafkaSession>());

        var cmd = new ConnectCommand(repo.Object, factory.Object);
        var result = await cmd.ExecuteAsync(Ctx("connect PROD"), CancellationToken.None);

        Assert.IsType<CommandResult.Text>(result);
    }

    [Fact]
    public async Task Status_reports_no_connection_when_there_is_none()
    {
        var result = await new StatusCommand().ExecuteAsync(Ctx("status"), CancellationToken.None);

        var text = Assert.IsType<CommandResult.Text>(result);
        Assert.Contains("No active connection", text.Message);
    }

    [Fact]
    public async Task Status_names_the_profile_when_connected()
    {
        var session = new Mock<IKafkaSession>();
        session.Setup(s => s.ProfileName).Returns("prod");
        session.Setup(s => s.BootstrapServers).Returns("k1:9092");

        var result = await new StatusCommand().ExecuteAsync(Ctx("status", session.Object), CancellationToken.None);

        var text = Assert.IsType<CommandResult.Text>(result);
        Assert.Contains("prod", text.Message);
        Assert.Contains("k1:9092", text.Message);
    }

    [Fact]
    public async Task Disconnect_names_the_session_it_is_closing()
    {
        var session = new Mock<IKafkaSession>();
        session.Setup(s => s.ProfileName).Returns("prod");

        var result = await new DisconnectCommand().ExecuteAsync(Ctx("disconnect", session.Object), CancellationToken.None);

        Assert.Contains("prod", Assert.IsType<CommandResult.Text>(result).Message);
    }
}

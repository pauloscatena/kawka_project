using Skat.KawkaProject.Core.Models;

namespace Skat.KawkaProject.Core.Tests;

public class DestructiveActionTests
{
    [Fact]
    public void Recreate_enumerates_what_a_shrink_recreate_destroys()
    {
        var action = DestructiveAction.Recreate("orders");

        Assert.Equal("orders", action.TopicName);
        Assert.Equal("recreate", action.Verb);
        // The two consequences the review confirmed: messages and consumer group offsets.
        Assert.Contains(action.WhatIsLost, w => w.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(action.WhatIsLost, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recreate_does_not_claim_ACLs_are_lost()
    {
        // Literal ACLs bound to the same topic name survive a delete+recreate, so listing them as
        // lost would send the user to re-grant permissions that were never revoked. This assertion
        // exists because the ITopicService doc-comment claimed otherwise until this list replaced it.
        Assert.DoesNotContain(DestructiveAction.Recreate("orders").WhatIsLost,
            w => w.Contains("ACL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Delete_says_nothing_survives()
    {
        var action = DestructiveAction.Delete("orders");

        Assert.Equal("delete", action.Verb);
        Assert.Contains(action.WhatIsLost, w => w.Contains("message", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(action.WhatIsLost, w => w.Contains("offset", StringComparison.OrdinalIgnoreCase));

        // A recreate preserves config overrides; a delete has nothing to preserve them on.
        Assert.Empty(action.WhatIsPreserved);
    }

    [Fact]
    public void Delete_does_not_promise_what_only_a_recreate_can_mean()
    {
        // The recreate wording explains that consumers may skip or replay, because the topic comes
        // back. After a delete there is nothing to skip or replay, and saying so would be wrong.
        Assert.DoesNotContain(DestructiveAction.DeleteLoses,
            w => w.Contains("auto.offset.reset", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Recreate_keeps_config_overrides_on_the_preserved_side()
    {
        var action = DestructiveAction.Recreate("orders");

        // Both halves travel together: a frontend that shows the losses without "config overrides
        // are kept" invites the user to re-apply settings the saga already carried over.
        Assert.Contains(action.WhatIsPreserved, w => w.Contains("config", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(action.WhatIsLost, w => w.Contains("config", StringComparison.OrdinalIgnoreCase));
    }
}

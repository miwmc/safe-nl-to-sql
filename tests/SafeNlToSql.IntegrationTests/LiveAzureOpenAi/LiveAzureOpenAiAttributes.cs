namespace SafeNlToSql.IntegrationTests.LiveAzureOpenAi;

// xUnit 2.9 cannot skip a test while it runs, so these decide during discovery, by setting Skip.

/// <summary>A [Fact] that is skipped unless the live tests are enabled, see <see cref="LiveAzureOpenAiConfig"/>.</summary>
public sealed class LiveAzureOpenAiFactAttribute : FactAttribute
{
    public LiveAzureOpenAiFactAttribute()
    {
        if (!LiveAzureOpenAiConfig.IsEnabled)
        {
            Skip = LiveAzureOpenAiConfig.SkipReason;
        }
    }
}

/// <summary>
/// A [Theory] that is skipped unless the live tests are enabled. xUnit reports a skipped theory once, not
/// once per row.
/// </summary>
public sealed class LiveAzureOpenAiTheoryAttribute : TheoryAttribute
{
    public LiveAzureOpenAiTheoryAttribute()
    {
        if (!LiveAzureOpenAiConfig.IsEnabled)
        {
            Skip = LiveAzureOpenAiConfig.SkipReason;
        }
    }
}

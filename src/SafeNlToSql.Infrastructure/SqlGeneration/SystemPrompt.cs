namespace SafeNlToSql.Infrastructure;

/// <summary>
/// The instructions sent to the model, kept in SystemPrompt.txt. They are fixed text: the user's question
/// is never added to them. The model can ignore them, so SqlGuard and the database enforce the rules.
/// </summary>
public static class SystemPrompt
{
    // Set explicitly in the project file, so moving the file or renaming a folder cannot change it.
    private const string ResourceName = "SafeNlToSql.Infrastructure.SystemPrompt.txt";

    private static readonly Lazy<string> Content = new(Load);

    public static string Text => Content.Value;

    private static string Load()
    {
        using var stream = typeof(SystemPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

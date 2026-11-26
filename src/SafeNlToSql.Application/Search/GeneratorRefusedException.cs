namespace SafeNlToSql.Application;

/// <summary>
/// Thrown by an <see cref="ISqlGenerator"/> when the model's service refuses the question or its answer, so
/// that asking again cannot help. The search is rejected; it is not a generator failure. The message is
/// logged, so it must not carry the question, model output or the service's error message.
/// </summary>
public sealed class GeneratorRefusedException(string message) : Exception(message);

namespace Structly.AI.Analyzers;

sealed class Issue(string path, string code, string message) : Exception
{
    public string Path { get; } = path;
    public string Code { get; } = code;
    public override string Message { get; } = message;
}

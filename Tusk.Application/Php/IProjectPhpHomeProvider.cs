namespace Tusk.Application.Php;

public interface IProjectPhpHomeProvider
{
    Task<ProjectPhpHome> EnsureCreatedAsync(string startDirectory, CancellationToken cancellationToken = default);
    Task<ProjectPhpHome?> TryGetExistingAsync(string startDirectory, CancellationToken cancellationToken = default);
}

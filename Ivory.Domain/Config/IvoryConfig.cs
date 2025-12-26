namespace Ivory.Domain.Config;

public sealed class IvoryConfig
{
    public string? Org { get; set; }
    public string? Project { get; set; }
    public PhpSection Php { get; init; } = new();
    public Dictionary<string, IvoryScript> Scripts { get; init; }
        = new(StringComparer.OrdinalIgnoreCase);

    public sealed class PhpSection
    {
        public string? Version { get; set; }
        public List<string> Ini { get; init; } = [];
        public List<string> Args { get; init; } = [];
    }

    public sealed class IvoryScript
    {
        // Composer-compatible: value is a string or array of strings.
        public List<string> Commands { get; init; } = [];
    }
}

public enum FrameworkKind
{
    Generic,
    Laravel,
    Symfony
}

public static class IvoryConfigFactory
{
    public static IvoryConfig CreateFor(FrameworkKind framework)
        => framework switch
        {
            FrameworkKind.Laravel => CreateLaravel(),
            FrameworkKind.Symfony => CreateSymfony(),
            _ => CreateGeneric()
        };

    private static IvoryConfig CreateGeneric()
    {
        return new IvoryConfig
        {
            Php = new IvoryConfig.PhpSection
            {
                Version = "8.3",
                Ini = { "display_errors=1" }
            },
            Scripts = new Dictionary<string, IvoryConfig.IvoryScript>(StringComparer.OrdinalIgnoreCase)
            {
                ["serve"] = new() { Commands = { "php -S localhost:8000 public/index.php --env=dev" } },
                ["test"] = new() { Commands = { "php vendor/bin/phpunit --colors=always" } }
            }
        };
    }

    private static IvoryConfig CreateLaravel()
    {
        return new IvoryConfig
        {
            Php = new IvoryConfig.PhpSection
            {
                Version = "8.3",
                Ini = { "display_errors=1" }
            },
            Scripts = new Dictionary<string, IvoryConfig.IvoryScript>(StringComparer.OrdinalIgnoreCase)
            {
                ["serve"] = new() { Commands = { "php artisan serve" } },
                ["migrate"] = new() { Commands = { "php artisan migrate" } },
                ["tinker"] = new() { Commands = { "php artisan tinker" } },
                ["queue:work"] = new() { Commands = { "php artisan queue:work" } }
            }
        };
    }

    private static IvoryConfig CreateSymfony()
    {
        return new IvoryConfig
        {
            Php = new IvoryConfig.PhpSection
            {
                Version = "8.3",
                Ini = { "display_errors=1" }
            },
            Scripts = new Dictionary<string, IvoryConfig.IvoryScript>(StringComparer.OrdinalIgnoreCase)
            {
                ["serve"] = new() { Commands = { "php -S 127.0.0.1:8000 public/index.php" } },
                ["console"] = new() { Commands = { "php bin/console" } },
                ["migrations:migrate"] = new() { Commands = { "php bin/console doctrine:migrations:migrate" } }
            }
        };
    }
}

using System.Reflection;
using System.Text.RegularExpressions;
using FluentMigrator.Runner;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace MSTKit.MigrationRunner;

public static class MigrationCli
{
    private const string AppSettingPath = "appsettings.json";
    private const string PersistenceConfigKey = "PersistenceConfig";

    public static void Run(string[] args, Assembly migrationsAssembly)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var settings = LoadSettings(args, baseDir);

            using var serviceProvider = CreateServices(settings, migrationsAssembly, out var logger);

            var connBuilder = new SqlConnectionStringBuilder(settings.ConnectionString);
            
            AnsiConsole.Write(new Rule("[#3B82F6]Database Setup & Migrations[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[#3B82F6]Target Server   :[/] {connBuilder.DataSource}");
            AnsiConsole.MarkupLine($"[#3B82F6]Target Database :[/] {connBuilder.InitialCatalog}");
            AnsiConsole.Write(new Rule().RuleStyle("#94A3B8").LeftJustified());

            EnsureDatabaseExists(settings.ConnectionString, logger);

            var runner = serviceProvider.GetRequiredService<IMigrationRunner>();
            var action = GetUserAction(args, settings.ConnectionString);

            switch (action.Type)
            {
                case MigrationActionType.Cancelled:
                    AnsiConsole.MarkupLine("[#94A3B8]No database changes were applied.[/]");
                    break;

                case MigrationActionType.RollbackOne:
                    AnsiConsole.MarkupLine("[#F59E0B]Executing Rollback (1 step)...[/]");
                    runner.Rollback(1);
                    AnsiConsole.MarkupLine("[#10B981]Rollback finished successfully.[/]");
                    break;

                case MigrationActionType.RollbackToVersion:
                    AnsiConsole.MarkupLine($"[#F59E0B]Reverting down to version[/] [#38BDF8]{action.TargetVersion}[/][#F59E0B]...[/]");
                    runner.MigrateDown(action.TargetVersion);
                    AnsiConsole.MarkupLine($"[#10B981]Reverted to version {action.TargetVersion} successfully.[/]");
                    break;

                case MigrationActionType.Up:
                default:
                    var runStartTime = DateTime.UtcNow.AddSeconds(-2);
                    AnsiConsole.MarkupLine("[#10B981]Applying pending migrations (Up)...[/]");
                    runner.MigrateUp();
                    AnsiConsole.MarkupLine("[#10B981]All migrations applied successfully.[/]");
                    ShowMigrationSummary(settings.ConnectionString, runStartTime);
                    break;
            }
        }
        catch (ConfigurationMissingException configEx)
        {
            Environment.ExitCode = 1;
            AnsiConsole.WriteLine();
            
            var panel = new Panel(configEx.Message)
                .Header("[#EF4444] Setup Required [/]")
                .BorderColor(Color.FromHex("#EF4444"))
                .Padding(2, 1);
                
            AnsiConsole.Write(panel);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[#EF4444 bold][FATAL][/] Migration failed:");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything); 
        }
        finally
        {
            if (!Console.IsInputRedirected && !args.Contains("--no-wait"))
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[#94A3B8]Press any key to exit...[/]");
                Console.ReadKey(true); 
            }
        }
    }

    private static MigrationAction GetUserAction(string[] args, string connectionString)
    {
        if (Console.IsInputRedirected || args.Contains("--auto") || args.Contains("--no-interactive"))
        {
            if (args.Contains("--rollback") || args.Contains("-r"))
                return new MigrationAction(MigrationActionType.RollbackOne);

            var target = args.FirstOrDefault(a => a.StartsWith("--target="));
            if (target != null && long.TryParse(target.Split('=')[1], out var ver))
                return new MigrationAction(MigrationActionType.RollbackToVersion, ver);

            return new MigrationAction(MigrationActionType.Up);
        }

        if (args.Contains("--rollback") || args.Contains("-r"))
            return ConfirmAction(new MigrationAction(MigrationActionType.RollbackOne), 
                "Are you sure you want to ROLLBACK the LAST migration?");

        var targetArg = args.FirstOrDefault(a => a.StartsWith("--target="));
        if (targetArg != null && long.TryParse(targetArg.Split('=')[1], out var targetVer))
            return ConfirmAction(new MigrationAction(MigrationActionType.RollbackToVersion, targetVer),
                $"Are you sure you want to ROLLBACK down to version {targetVer}?");

        AnsiConsole.WriteLine();
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("[#3B82F6]Choose Migration Mode:[/]")
                .PageSize(4)
                .HighlightStyle("#3B82F6")
                .AddChoices(
                    "Migrate UP to latest",
                    "Rollback 1 step (Down)",
                    "Rollback to a specific Version",
                    "Cancel"
                ));

        if (choice == "Rollback 1 step (Down)")
        {
            return ConfirmAction(new MigrationAction(MigrationActionType.RollbackOne), 
                "Are you sure you want to ROLLBACK the LAST migration?");
        }

        if (choice == "Rollback to a specific Version")
        {
            while (true)
            {
                var verInput = AnsiConsole.Ask<string>("[#38BDF8]Enter Target Version[/] [#94A3B8](or type 'list' / 'c' to cancel):[/] ").Trim();

                if (string.Equals(verInput, "c", StringComparison.OrdinalIgnoreCase))
                {
                    AnsiConsole.MarkupLine("[#94A3B8]Operation cancelled.[/]");
                    return new MigrationAction(MigrationActionType.Cancelled);
                }

                if (string.Equals(verInput, "l", StringComparison.OrdinalIgnoreCase) || 
                    string.Equals(verInput, "list", StringComparison.OrdinalIgnoreCase))
                {
                    ShowAppliedVersions(connectionString);
                    continue;
                }

                if (long.TryParse(verInput, out var ver))
                {
                    return ConfirmAction(new MigrationAction(MigrationActionType.RollbackToVersion, ver), 
                        $"Are you sure you want to ROLLBACK down to version {ver}?");
                }

                AnsiConsole.MarkupLine("[#EF4444]Invalid version number. Try again.[/]");
            }
        }

        if (choice == "Cancel")
        {
            AnsiConsole.MarkupLine("[#94A3B8]Operation cancelled.[/]");
            return new MigrationAction(MigrationActionType.Cancelled);
        }

        return new MigrationAction(MigrationActionType.Up);
    }

    private static void ShowAppliedVersions(string connectionString)
    {
        AnsiConsole.WriteLine();

        var sql = @"
            IF OBJECT_ID(N'VersionInfo', N'U') IS NOT NULL
                SELECT Version, AppliedOn, Description 
                FROM VersionInfo 
                ORDER BY Version DESC;
            ELSE
                SELECT 0 AS Version, NULL AS AppliedOn, 'No VersionInfo Table' AS Description WHERE 1=0;";

        try
        {
            using var connection = new SqlConnection(connectionString);
            using var command = new SqlCommand(sql, connection);
            connection.Open();
            using var reader = command.ExecuteReader();

            if (!reader.HasRows)
            {
                AnsiConsole.MarkupLine("[#94A3B8]No applied migrations found in VersionInfo table.[/]");
                AnsiConsole.WriteLine();
                return;
            }

            var table = new Table()
                .Border(TableBorder.Rounded)
                .Title("[#3B82F6]Applied Migrations in Database[/]")
                .AddColumn(new TableColumn("[#94A3B8]Version[/]").Centered())
                .AddColumn(new TableColumn("[#94A3B8]Applied On[/]").Centered())
                .AddColumn(new TableColumn("[#94A3B8]Description[/]"));

            while (reader.Read())
            {
                var ver = reader["Version"]?.ToString() ?? "-";
                var date = reader["AppliedOn"] is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss") : "-";
                var desc = reader["Description"]?.ToString() ?? "-";

                table.AddRow($"[#38BDF8]{ver}[/]", date, $"[#94A3B8]{desc}[/]");
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[#EF4444]Failed to read VersionInfo: {ex.Message}[/]");
        }

        AnsiConsole.WriteLine();
    }
    
    private static void ShowMigrationSummary(string connectionString, DateTime startTime)
    {
        var sql = @"
            IF OBJECT_ID(N'VersionInfo', N'U') IS NOT NULL
                SELECT Version, Description 
                FROM VersionInfo 
                WHERE AppliedOn >= @StartTime
                ORDER BY Version ASC;";

        try
        {
            using var connection = new SqlConnection(connectionString);
            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@StartTime", startTime);
            connection.Open();
            using var reader = command.ExecuteReader();

            if (!reader.HasRows)
            {
                AnsiConsole.MarkupLine("\n[#94A3B8]No new migrations were applied (Database is already up to date).[/]");
                return;
            }

            AnsiConsole.WriteLine();
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.FromHex("#3B82F6"))
                .Title("[#3B82F6]Execution Summary[/]")
                .AddColumn(new TableColumn("[#94A3B8]Version[/]").Centered())
                .AddColumn(new TableColumn("[#94A3B8]Description[/]"))
                .AddColumn(new TableColumn("[#94A3B8]Status[/]").Centered());

            while (reader.Read())
            {
                var ver = reader["Version"]?.ToString() ?? "-";
                var desc = reader["Description"]?.ToString() ?? "-";
                
                table.AddRow($"[#38BDF8]{ver}[/]", $"[#94A3B8]{desc}[/]", "[#10B981]Success[/]");
            }

            AnsiConsole.Write(table);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[#EF4444]Failed to generate summary: {ex.Message}[/]");
        }
    }

    private static MigrationAction ConfirmAction(MigrationAction action, string warningMessage)
    {
        AnsiConsole.WriteLine();
        var isConfirmed = AnsiConsole.Confirm($"[#F59E0B][[WARNING]][/] {warningMessage}", defaultValue: false);

        if (isConfirmed) return action;

        AnsiConsole.MarkupLine("[#94A3B8]Operation cancelled by user.[/]");
        return new MigrationAction(MigrationActionType.Cancelled);
    }

    private static void EnsureDatabaseExists(string connectionString, ILogger logger)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var dbName = builder.InitialCatalog;

        if (string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException("InitialCatalog is missing from ConnectionString.");

        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };

        var safeDbName = dbName.Replace("]", "]]");
        var sql = $@"
            IF DB_ID(N'{safeDbName}') IS NULL 
            BEGIN
                CREATE DATABASE [{safeDbName}];
            END";

        using var connection = new SqlConnection(masterBuilder.ConnectionString);
        using var command = new SqlCommand(sql, connection);

        try
        {
            connection.Open();
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to connect or create database '{DbName}'.", dbName);
            throw;
        }
    }

    private static ServiceProvider CreateServices(MigrationSettings settings, Assembly migrationsAssembly, out ILogger logger)
    {
        var services = new ServiceCollection();

        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddProvider(new SpectreLoggerProvider());
            loggingBuilder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSqlServer()
                .WithGlobalConnectionString(settings.ConnectionString)
                .ScanIn(migrationsAssembly).For.All());

        services.AddSingleton(settings);

        var provider = services.BuildServiceProvider();
        logger = provider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MigrationRunner");

        return provider;
    }

    private static MigrationSettings LoadSettings(string[] args, string basePath)
    {
        var configFilePath = Path.Combine(basePath, AppSettingPath);

        if (!File.Exists(configFilePath))
        {
            throw new ConfigurationMissingException(
                $"Could not find [#F59E0B]{AppSettingPath}[/] in the application directory.\n\n" +
                $"[#94A3B8]Please create this file and add your database configuration:[/]\n" +
                $"[#38BDF8]{{\n" +
                $"  \"{PersistenceConfigKey}\": {{\n" +
                $"    \"ConnectionString\": \"Server=YOUR_SERVER;Database=YOUR_DB;User Id=...;Password=...;TrustServerCertificate=True\"\n" +
                $"  }}\n" +
                $"}}[/]");
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile(AppSettingPath, optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var settings = new MigrationSettings();
        config.Bind(PersistenceConfigKey, settings);

        if (string.IsNullOrWhiteSpace(settings.ConnectionString))
        {
            throw new ConfigurationMissingException(
                $"The [#F59E0B]ConnectionString[/] is missing or empty in your [#F59E0B]{AppSettingPath}[/].\n\n" +
                $"[#94A3B8]Make sure your JSON structure looks exactly like this:[/]\n" +
                $"[#38BDF8]{{\n" +
                $"  \"{PersistenceConfigKey}\": {{\n" +
                $"    \"ConnectionString\": \"Server=YOUR_SERVER;Database=YOUR_DB;...\"\n" +
                $"  }}\n" +
                $"}}[/]");
        }

        return settings;
    }

    private enum MigrationActionType { Up, RollbackOne, RollbackToVersion, Cancelled }

    private record MigrationAction(MigrationActionType Type, long TargetVersion = 0);
}

public class MigrationSettings
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class ConfigurationMissingException : Exception
{
    public ConfigurationMissingException(string message) : base(message) { }
}

public class SpectreLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new SpectreLogger();
    public void Dispose() { }
}

public class SpectreLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message)) return;

        var safeMessage = Markup.Escape(message);

        if (logLevel >= LogLevel.Error || exception != null)
        {
            AnsiConsole.MarkupLine($"[#EF4444 bold][ERROR][/] {safeMessage}");
            if (exception != null) AnsiConsole.WriteException(exception);
            return;
        }

        if (safeMessage.StartsWith("=&gt;")) 
        {
            var time = safeMessage.Replace("=&gt;", "").Trim();
            AnsiConsole.MarkupLine($"    [#94A3B8]Time:[/] [#0EA5E9]{time}[/]\n");
        }
        else if (safeMessage.Contains("migrating"))
        {
            AnsiConsole.Write(new Rule($"[#3B82F6]{safeMessage}[/]").RuleStyle("#94A3B8").LeftJustified());
        }
        else if (safeMessage.Contains("migrated"))
        {
            AnsiConsole.MarkupLine($"    [#10B981]{safeMessage}[/]");
        }
        else
        {
            var highlightedSql = safeMessage;
            
            highlightedSql = Regex.Replace(highlightedSql, @"\b(BEGIN TRANSACTION|COMMIT TRANSACTION|Beginning Transaction|Committing Transaction)\b", "[#0EA5E9]$1[/]");
            highlightedSql = Regex.Replace(highlightedSql, @"\b(ROLLBACK TRANSACTION|Rolling back transaction)\b", "[#F59E0B]$1[/]");
            
            highlightedSql = Regex.Replace(highlightedSql, @"\b(CreateTable|AlterTable|CreateColumn|CreateForeignKey|CreateConstraint|CreateIndex)\b", "[#94A3B8]$1[/]");
            
            highlightedSql = Regex.Replace(highlightedSql, @"\b(CREATE TABLE|ALTER TABLE|DROP TABLE|INSERT INTO|PRIMARY KEY|FOREIGN KEY|REFERENCES|CONSTRAINT|NOT NULL|UNIQUE|DEFAULT|VALUES|ON DELETE|ON UPDATE|CASCADE|SET NULL|ASC|DESC)\b", "[#8B5CF6]$1[/]");
            
            highlightedSql = Regex.Replace(highlightedSql, @"\b(IDENTITY|NVARCHAR|DATETIME2|DATETIME|DECIMAL|BIGINT|TINYINT|INT)\b", "[#38BDF8]$1[/]");

            AnsiConsole.MarkupLine($"    [#94A3B8]{highlightedSql}[/]");
        }
    }
}
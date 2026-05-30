using Npgsql;
using System;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using RDesigner.Resources;
using Serilog;

namespace RDesigner.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string DBName { get; init; } = string.Empty;

    public string Port { get; init; } = string.Empty;

    public string Host { get; init; } = "localhost";

    public string CreateConnectionString()
    {
        if (!int.TryParse(Port, out var port))
        {
            throw new InvalidOperationException(AppStrings.DatabasePortMustBeValidInteger);
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Database = DBName,
            Port = port,
            Username = Username,
            Password = Password
        };

        return builder.ConnectionString;
    }
}

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var errors = new List<string>();

        AddRequiredError(errors, options.Username, "Username");
        AddRequiredError(errors, options.Password, "Password");
        AddRequiredError(errors, options.DBName, "DBName");
        AddRequiredError(errors, options.Port, "Port");

        if (!string.IsNullOrWhiteSpace(options.Port) && !int.TryParse(options.Port, out _))
        {
            errors.Add(AppStrings.DatabasePortMustBeNumeric);
        }

        if (errors.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        Log.Error(AppStrings.InvalidDatabaseSettingsLog, string.Join(" ", errors));
        return ValidateOptionsResult.Fail(errors);
    }

    private static void AddRequiredError(ICollection<string> errors, string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(string.Format(AppStrings.DatabaseOptionMustNotBeEmptyFormat, optionName));
        }
    }
}

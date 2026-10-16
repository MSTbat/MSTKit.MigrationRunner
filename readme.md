# MSTKit.MigrationRunner

![NuGet Version](https://img.shields.io/nuget/v/MSTKit.MigrationRunner?style=flat-square&color=3B82F6)
![NuGet Downloads](https://img.shields.io/nuget/dt/MSTKit.MigrationRunner?style=flat-square&color=10B981)
![License](https://img.shields.io/github/license/MSTbat/MSTKit.MigrationRunner?style=flat-square)

An elegant, enterprise-grade, standalone **FluentMigrator** runner powered by **Spectre.Console**. 

`MSTKit.MigrationRunner` wraps the standard FluentMigrator engine in a beautiful, interactive CLI with a modern "DevOps Classic" color palette. Say goodbye to boring, unreadable database migration logs!

---

## ✨ Features

- **Beautiful UI:** Replaces standard console output with highlighted SQL syntax, transaction states, and execution times using Spectre.Console.
- **Auto-Database Creation:** Automatically detects if the target SQL Server database exists and creates it if it doesn't.
- **Idiot-Proof Configuration:** Provides clear, visual error panels if `appsettings.json` or connection strings are missing.
- **Interactive & Headless Modes:** Perfect for both local development (interactive prompts) and CI/CD pipelines (headless execution).
- **Safe Rollbacks:** Easily rollback the last migration or revert to a specific version safely.

## 📦 Installation

Install the package via NuGet Package Manager Console:

```bash
Install-Package MSTKit.MigrationRunner
```

Or via the .NET Core command-line interface:

```bash
dotnet add package MSTKit.MigrationRunner
```

## 🚀 Quick Start

### 1. Configure your Application

In your target project (e.g., a Console App), simply call the `MigrationCli.Run` method and pass your arguments and the assembly containing your FluentMigrator migration classes.

```csharp
using System.Reflection;
using MSTKit.MigrationRunner;

namespace YourApp.DatabaseSetup;

public class Program
{
    public static void Main(string[] args)
    {
        // Pass CLI arguments and the assembly containing your migrations
        MigrationCli.Run(args, Assembly.GetExecutingAssembly());
    }
}
```

### 2. Add Configuration

Create an `appsettings.json` file in the root of your executable project. Ensure its properties are set to **"Copy to Output Directory: Copy if newer"**.

```json
{
  "PersistenceConfig": {
    "ConnectionString": "Server=localhost;Database=MyAwesomeDb;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True"
  }
}
```

### 3. Write a Migration

Create a standard FluentMigrator class in your project:

```csharp
using FluentMigrator;

namespace YourApp.DatabaseSetup.Migrations;

[Migration(202610171200)]
public class InitialMigration : Migration
{
    public override void Up()
    {
        Create.Table("Users")
            .WithColumn("Id").AsInt32().PrimaryKey().Identity()
            .WithColumn("Username").AsString(50).NotNullable();
    }

    public override void Down()
    {
        Delete.Table("Users");
    }
}
```

## 🛠️ CLI Usage

Run your application from the terminal. If you run it without arguments, it will present a beautiful interactive menu.

### Interactive Menu
```bash
dotnet run
```
*Prompts you to choose: Migrate UP, Rollback 1 step, Rollback to version, or Cancel.*

### Non-Interactive (CI/CD Pipelines)

You can bypass the interactive menu by passing arguments (great for GitHub Actions / Docker):

- **Migrate Up to Latest:**
  ```bash
  dotnet run -- --auto
  ```

- **Rollback 1 Step:**
  ```bash
  dotnet run -- --rollback
  # or
  dotnet run -- -r
  ```

- **Rollback to Specific Version:**
  ```bash
  dotnet run -- --target=202610171200 --auto
  ```

- **Run without waiting for user input at the end:**
  ```bash
  dotnet run -- --no-wait
  ```


## 📄 License

This project is licensed under the MIT License. See the LICENSE file for details.
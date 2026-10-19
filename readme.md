# MSTKit.MigrationRunner

![NuGet Version](https://img.shields.io/nuget/v/MSTKit.MigrationRunner?style=flat-square&color=3B82F6&v=1.0.2)
![NuGet Downloads](https://img.shields.io/nuget/dt/MSTKit.MigrationRunner?style=flat-square&color=10B981&v=1.0.2)
![License](https://img.shields.io/github/license/MSTbat/MSTKit.MigrationRunner?style=flat-square&v=1.0.2)

An elegant, ultra-lightweight, and standalone **FluentMigrator** runner powered by **Spectre.Console**.

`MSTKit.MigrationRunner` wraps the standard FluentMigrator engine in a beautiful, interactive CLI with a modern "DevOps Classic" color palette. Optimized specifically for **SQL Server**, it keeps your deployment size minimal without bloating your project with unnecessary database drivers. Say goodbye to boring, unreadable database migration logs!

---

## ✨ Features

- **SQL Server Optimized:** Strictly targeted for SQL Server, keeping your publish output extremely lightweight (no bloated transitive drivers like DB2 or Firebird).
- **Beautiful UI:** Replaces standard console output with highlighted SQL syntax, transaction states, and execution times using Spectre.Console.
- **Auto-Database Creation:** Automatically detects if the target SQL Server database exists and creates it if it doesn't.
- **Idiot-Proof Configuration:** Provides clear, visual error panels if `appsettings.json` or connection strings are missing.
- **Interactive & Headless Modes:** Perfect for both local development (interactive prompts) and CI/CD pipelines (headless execution).

## 📦 Installation

Install the package via NuGet Package Manager Console:

```bash
Install-Package MSTKit.MigrationRunner
```

Or via the .NET Core CLI:

```bash
dotnet add package MSTKit.MigrationRunner
```

## 🚀 Quick Start

### 1. Configure your Application
In your target project (e.g., a Console App), simply call the `MigrationCli.Run` method and pass your arguments along with the assembly containing your FluentMigrator classes.

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
Create an `appsettings.json` file in the root of your executable project. Ensure its properties are set to **"Copy to Output Directory: Preserve Newest"**.

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

`MSTKit.MigrationRunner` is smart. It detects whether you are running it locally or inside an automated CI/CD pipeline and adapts its behavior accordingly.

### 🎮 Interactive Menu (Local Development)
Simply run the application without any arguments to launch the interactive menu. Use your keyboard's **Up/Down Arrow keys** to navigate and press **Enter** to select:

```bash
dotnet run
```

```text
? Choose Migration Mode:
> Migrate UP to latest
  Rollback 1 step (Down)
  Rollback to a specific Version
  Cancel
```

- **Migrate UP to latest:** Automatically finds all pending migrations and applies them.
- **Rollback 1 step (Down):** Safely reverts the *last* applied migration. It will prompt for confirmation before executing.
- **Rollback to a specific Version:** Shows a formatted table of all applied migrations and lets you input the exact version number you want to revert to.
- **Cancel:** Exits gracefully without touching the database.

### 🤖 Non-Interactive (CI/CD Pipelines / Docker)
When running in automated environments, bypass the interactive menu by explicitly passing arguments:

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
This project is licensed under the MIT License. See the `LICENSE` file for details.
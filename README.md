# 🚗 Garij — Intelligent Vehicle Service Center Management System

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-3--Layer%20Clean-blue)](CONTEXT.md)
[![Database](https://img.shields.io/badge/Database-SQLite%20%7C%20SQL%20Server-green)](https://learn.microsoft.com/ef/core/)
[![License](https://img.shields.io/badge/License-MIT-orange.svg)](LICENSE)

**Garij** is a state-of-the-art enterprise web application designed to digitize and automate operations for vehicle service centers. It manages customer and vehicle records, service job intake, mechanic assignments, parts inventory tracking, invoice billing, real-time notifications, AI-powered predictive diagnostics, and a public booking status lookup portal.

---

## 🏗️ Architecture & Technology Stack

The project follows a clean **3-Layer Architecture** (Presentation, Application, Domain, Infrastructure) ensuring strict separation of concerns, high maintainability, and testability.

```
┌─────────────────────────────────────────────────────────────┐
│                 Garij.Web (ASP.NET Core MVC)                │
│   Controllers, Views, Identity UI, Middleware, DI Wiring     │
└──────────────┬──────────────────────────────┬───────────────┘
               │                              │
               ▼                              ▼
┌──────────────────────────────┐  ┌───────────────────────────┐
│     Garij.Application        │  │   Garij.Infrastructure    │
│ Service Logic, DTOs, Contracts│  │ EF Core DbContext, Repos, │
└──────────────┬───────────────┘  │ Identity, External APIs   │
               │                  └──────────────┬────────────┘
               ▼                                 ▼
┌─────────────────────────────────────────────────────────────┐
│                      Garij.Domain                           │
│           Core Entities, Enums, Domain Exceptions           │
└─────────────────────────────────────────────────────────────┘
```

- **Framework**: .NET 10.0 ASP.NET Core MVC
- **Data Access**: Entity Framework Core 10.0 (Dual support for SQLite local development and SQL Server production)
- **Security & Auth**: ASP.NET Core Identity with Role-Based Access Control (`Admin`, `Receptionist`, `Mechanic`, `Customer`)
- **AI Intelligence Engine**: Integrated Google Gemini API template for predictive maintenance and duration estimation
- **Testing**: xUnit unit & integration testing framework (`Garij.UnitTests`, `Garij.IntegrationTests`)
- **Exception Handling**: Global exception middleware returning structured JSON for AJAX and friendly views for web requests

---

## 🌟 Key Features & Business Workflows

- **Touch-Friendly Mechanic Job Board & State Machine**: Mobile-optimized dashboard for mechanics to manage assigned repair tasks, record diagnostic notes, and advance jobs through strict, validated state transitions (`Requested` &rarr; `InspectionPending` &rarr; `CustomerApprovalNeeded` &rarr; `InProgress` &rarr; `Completed`).
- **Enforced Business Integrity Rules**: Hard pre-condition validation preventing job completion without logged parts used (`BR-008`), and enforcing lead mechanic uniqueness (`BR-003`).
- **Transactional Invoice Generation**: Automated invoice calculation summing labor and logged parts with atomic database rollback protections (`BR-007`).
- **Customer & Vehicle Intake**: Streamlined registration, unique booking reference generation (`GRJ-2026-XXXX`), and service history tracking.
- **Parts Inventory & Reorder Alerts**: Inventory management with atomic stock deduction and automatic low-stock alerts.
- **Public Booking Status Portal**: Mobile-first public portal for customers to track real-time repair progress via booking reference or license plate.

---

## 📋 Prerequisites

Before running the project, ensure you have installed:
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- Git
- Any code editor: Visual Studio 2022 (v17.12+), JetBrains Rider, or VS Code with C# Dev Kit

---

## ⚡ Quick Start & How to Run

### 1. Clone the Repository
```bash
git clone https://github.com/AftabAhmedFahim/garij.git
cd garij
```

### 2. Build the Solution
```bash
dotnet build Garij.sln
```

### 3. Run the Web Application
```bash
dotnet run --project src/Garij.Web
```

> **Note**: On startup, the application will automatically create the database (`Garij.db`), apply schema definitions, and seed default user roles.

Open your browser and navigate to:
```
http://localhost:5099
```

### 4. Run Tests
Execute all unit and integration test suites:
```bash
dotnet test Garij.sln
```

---

## 🗄️ Database Configuration

The application is configured out of the box with **cross-platform database support**:

- **Local Development (Linux / macOS / Windows)**:
  Uses SQLite database file (`src/Garij.Web/Garij.db`). No database server setup required!
- **Production / SQL Server**:
  To switch to SQL Server, update `src/Garij.Web/appsettings.json`:
  ```json
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=GarijDb;User Id=sa;Password=YourPassword123!;TrustServerCertificate=True;"
  }
  ```

---

## 📂 Project Structure

```
garij/
├── Garij.sln                  # Main Visual Studio Solution
├── Garij.slnx                 # .NET 10 Solution Manifest
├── CONTEXT.md                 # AI Assistant & Developer Context Log
├── README.md                  # Project Documentation
├── src/
│   ├── Garij.Domain/          # Core Domain Entities, Enums, Custom Exceptions
│   ├── Garij.Application/     # Interfaces, DTOs, Business Logic Services
│   ├── Garij.Infrastructure/  # EF Core DbContext, Repositories, Migrations
│   └── Garij.Web/             # MVC Controllers, Views, Middleware, Program.cs
└── tests/
    ├── Garij.UnitTests/       # Unit Tests (Domain, Logic)
    ├── Garij.IntegrationTests/# Integration Tests (Web Pipeline, DI)
    └── Garij.Tests/           # Additional Test Suites
```

---

## 🤖 Smart Intake Gemini AI Assistant Setup (Issue #31)

Garij includes a Google Gemini-backed Smart Intake Assistant that grounds diagnostic recommendations directly on the workshop's live `ServiceCatalog`. All suggestions are strictly **advisory** and require explicit staff confirmation before being appended into service job diagnostic notes.

### 1. Obtaining an API Key
1. Go to [Google AI Studio](https://aistudio.google.com/).
2. Sign in with your Google account and click **Get API Key**.
3. Create a new API key.

### 2. Configuring via User Secrets (Local Development)
Never commit your API key to source control. Set your secret locally in `src/Garij.Web`:

```bash
dotnet user-secrets set "GeminiSettings:ApiKey" "<YOUR_GEMINI_API_KEY>" --project src/Garij.Web
```

Verify your secret is active:
```bash
dotnet user-secrets list --project src/Garij.Web
```

If the API key is not configured, the assistant degrades gracefully with a helpful advisory message on the intake form without breaking normal job creation.

### 3. Swapping to Another LLM Provider
The AI engine is architected around the provider-agnostic `ILlmClient` interface (`Garij.Infrastructure.ExternalServices.Gemini.ILlmClient`).
To switch from Google Gemini to OpenRouter, Anthropic, or local Ollama:
1. Create a new implementation of `ILlmClient` (e.g. `OpenRouterClient.cs` or `OllamaClient.cs`) in `src/Garij.Infrastructure/ExternalServices/`.
2. In `src/Garij.Web/Program.cs`, swap the typed client registration:
   ```csharp
   builder.Services.AddHttpClient<ILlmClient, OpenRouterClient>(client => { client.Timeout = TimeSpan.FromSeconds(30); });
   ```
The core `IntelligenceService` business logic and UI partial remain 100% unchanged.

---

## 📄 License


This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

# Garij Cloud Deployment Brief: Render Deployment Documentation

This document records the exact verified configuration, code changes, repository history, and deployment procedures used to host the **Garij** Intelligent Vehicle Service Center Management System (.NET 10, ASP.NET Core MVC, EF Core, ASP.NET Core Identity, Google Gemini AI) on Render at:
**`https://garij.onrender.com/`**

This file serves as the definitive source of truth for writing the *"System Deployment Procedures"* chapter of the final project report.

---

## PART 1 — FROM THE CODE (Verified Technical Baseline)

### 1. Fork vs Upstream

#### Repositories and Branches
- **Upstream Repository:** `https://github.com/AftabAhmedFahim/garij.git`
- **Upstream Branch:** `main`
- **Deployment Fork Repository:** `https://github.com/SHOEBILL04/Garij.git`
- **Branch Render Deploys From:** `main`

#### Git History of Deployment Commits
The fork repository was synchronized with upstream `main` up to pull request `#96` (`1c471403436dda89f95a07ab1753872b2dce6e5a`) via GitHub sync PR `#4` (`5c3eb452189a2d0d389be35a3d067305f2f13462`).

A single dedicated deployment commit was authored directly on `main` of the fork:

```text
commit b2d6371638bef7ac5317858dcec4a59abb0603fb (HEAD -> main, origin/main)
Author: Rakibul Islam <rakibulislamemon75@gmail.com>
Date:   Fri Sep 18 12:20:30 2026 +0600

    Add Dockerfile and cloud hosting configuration
```

**Summary of what commit `b2d6371` changed:**
1. Created `.dockerignore` to keep repository build files, development databases, and local artifacts out of Docker build contexts.
2. Created a production-grade multi-stage `Dockerfile` targeting the `.NET 10.0` SDK and ASP.NET Core runtime.
3. Updated `src/Garij.Web/Program.cs` with cloud reverse-proxy and hosting support:
   - Dynamically binds Kestrel to the port assigned by the host environment (`PORT` environment variable).
   - Configures `ForwardedHeadersOptions` to forward proxy headers (`X-Forwarded-For` and `X-Forwarded-Proto`) and clears known proxy IP filters so that Render's edge reverse proxy is trusted.
   - Activates `app.UseForwardedHeaders()`.

#### Preceding Synchronization Commit
```text
commit 5c3eb452189a2d0d389be35a3d067305f2f13462
Merge: 227c957 1c47140
Author: Rakibul_islam <130771331+SHOEBILL04@users.noreply.github.com>
Date:   Fri Sep 18 11:59:20 2026 +0600

    Merge pull request #4 from AftabAhmedFahim/main
```

#### File-by-File Differences Between Upstream `main` and Fork `main` for Deployment

Only three files were created or modified specifically for deployment:
1. `.dockerignore` (New File)
2. `Dockerfile` (New File)
3. `src/Garij.Web/Program.cs` (Modified)

All other system components were verified to have **zero differences** between upstream and the deployed fork:
- `src/Garij.Infrastructure/SeedData/DbSeeder.cs`: Identical to upstream.
- `src/Garij.Infrastructure/DependencyInjection.cs`: Identical to upstream.
- `src/Garij.Infrastructure/Garij.Infrastructure.csproj`: Identical to upstream.
- `src/Garij.Web/Garij.Web.csproj`: Identical to upstream.
- `src/Garij.Web/appsettings.json`: Identical to upstream.
- `appsettings.Production.json`: **NOT FOUND** (does not exist in upstream or in the fork).
- `render.yaml` (Blueprint): **NOT FOUND** (deployment was configured manually via Render Web UI).

*(Note: Upstream subsequently merged PR #97 on September 18, 2026, which touched 5 front-end UI files `_LandingLayout.cshtml`, `_Layout.cshtml`, `_LoginPartial.cshtml`, `landing.css`, `site.css` after this deployment fork branched).*

#### Relevant Diff Hunks for Deployment (`b2d6371` vs parent `5c3eb45`)

```diff
diff --git a/.dockerignore b/.dockerignore
new file mode 100644
index 0000000..9386142
--- /dev/null
+++ b/.dockerignore
@@ -0,0 +1,13 @@
+**/.git
+**/.github
+**/.vs
+**/.vscode
+**/bin
+**/obj
+**/TestResults
+**/tests
+**/*.db
+**/*.db-shm
+**/*.db-wal
+Dockerfile
+.dockerignore
diff --git a/Dockerfile b/Dockerfile
new file mode 100644
index 0000000..652682d
--- /dev/null
+++ b/Dockerfile
@@ -0,0 +1,34 @@
+# Stage 1: Build & Publish
+FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
+WORKDIR /src
+
+# Copy project files for caching dependency layers
+COPY ["src/Garij.Domain/Garij.Domain.csproj", "src/Garij.Domain/"]
+COPY ["src/Garij.Application/Garij.Application.csproj", "src/Garij.Application/"]
+COPY ["src/Garij.Infrastructure/Garij.Infrastructure.csproj", "src/Garij.Infrastructure/"]
+COPY ["src/Garij.Web/Garij.Web.csproj", "src/Garij.Web/"]
+
+# Restore NuGet dependencies
+RUN dotnet restore "src/Garij.Web/Garij.Web.csproj"
+
+# Copy source code and build
+COPY src/ src/
+WORKDIR "/src/src/Garij.Web"
+RUN dotnet publish "Garij.Web.csproj" -c Release -o /app/publish --no-restore
+
+# Stage 2: Runtime image
+FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
+WORKDIR /app
+
+# Ensure SQLite can create files in working directory
+RUN mkdir -p /app/data && chown -R app:app /app
+
+USER app
+COPY --from=build --chown=app:app /app/publish .
+
+# Default configuration for cloud hosting
+ENV ASPNETCORE_URLS=http://+:8080
+ENV ASPNETCORE_ENVIRONMENT=Production
+EXPOSE 8080
+
+ENTRYPOINT ["dotnet", "Garij.Web.dll"]
diff --git a/src/Garij.Web/Program.cs b/src/Garij.Web/Program.cs
index 98f2d47..68b39fa 100644
--- a/src/Garij.Web/Program.cs
+++ b/src/Garij.Web/Program.cs
@@ -8,6 +8,21 @@ using Microsoft.AspNetCore.Identity;
 
 var builder = WebApplication.CreateBuilder(args);
 
+// Cloud / Container hosting support: dynamically bind to PORT if provided (e.g. Render, Railway, Koyeb, Cloud Run)
+var port = Environment.GetEnvironmentVariable("PORT");
+if (!string.IsNullOrEmpty(port))
+{
+    builder.WebHost.UseUrls($"http://*:{port}");
+}
+
+// Support reverse proxies (Render, Fly.io, Cloudflare) for HTTPS detection
+builder.Services.Configure<ForwardedHeadersOptions>(options =>
+{
+    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
+    options.KnownIPNetworks.Clear();
+    options.KnownProxies.Clear();
+});
+
 // Add services to the container.
 builder.Services.AddInfrastructure(builder.Configuration);
 builder.Services.AddApplication();
@@ -46,6 +61,8 @@ builder.Services.AddControllersWithViews(options =>
 
 var app = builder.Build();
 
+app.UseForwardedHeaders();
+
 // Catches unhandled exceptions and renders the shared error view in place, keeping the
 // status code it calculated. Registered first so it wraps the whole pipeline, and used in
 // every environment so a failure looks the same to the user in development and production.
```

---

### 2. Build and Runtime Method

Because Render provides no native .NET 10 managed runtime environment (only Node, Python, Ruby, Go, Rust), the application must be built and run using a Docker container.

#### Blueprint Status
- `render.yaml`: **NOT FOUND**. Deployment was configured directly in the Render Web Console as a Docker Web Service.

#### Complete `.dockerignore` Content
*(Path: `/.dockerignore`)*
```dockerignore
**/.git
**/.github
**/.vs
**/.vscode
**/bin
**/obj
**/TestResults
**/tests
**/*.db
**/*.db-shm
**/*.db-wal
Dockerfile
.dockerignore
```

#### Complete `Dockerfile` Content
*(Path: `/Dockerfile`)*
```dockerfile
# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for caching dependency layers
COPY ["src/Garij.Domain/Garij.Domain.csproj", "src/Garij.Domain/"]
COPY ["src/Garij.Application/Garij.Application.csproj", "src/Garij.Application/"]
COPY ["src/Garij.Infrastructure/Garij.Infrastructure.csproj", "src/Garij.Infrastructure/"]
COPY ["src/Garij.Web/Garij.Web.csproj", "src/Garij.Web/"]

# Restore NuGet dependencies
RUN dotnet restore "src/Garij.Web/Garij.Web.csproj"

# Copy source code and build
COPY src/ src/
WORKDIR "/src/src/Garij.Web"
RUN dotnet publish "Garij.Web.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Ensure SQLite can create files in working directory
RUN mkdir -p /app/data && chown -R app:app /app

USER app
COPY --from=build --chown=app:app /app/publish .

# Default configuration for cloud hosting
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Garij.Web.dll"]
```

#### Detailed Breakdown of Dockerfile Stages

##### Stage 1: Build & Publish (`FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build`)
- **Base Image:** `mcr.microsoft.com/dotnet/sdk:10.0`. This is the official Microsoft image containing the full .NET 10 Software Development Kit (compilers, build engines, and NuGet package tools).
- **Working Directory:** `/src`.
- **Step 1 — Dependency Layer Caching:** Only the four `.csproj` project files are copied first. By copying only project files before the code, Docker saves a cached copy of the downloaded packages. If you edit C# code later, Docker does not need to download the NuGet packages again.
- **Step 2 — Package Restore:** `RUN dotnet restore "src/Garij.Web/Garij.Web.csproj"` downloads all required external libraries and packages for the web project and referenced projects.
- **Step 3 — Source Copy:** `COPY src/ src/` copies the remaining C# source files, Razor templates, CSS, and web assets into the image.
- **Step 4 — Compile and Publish:** Sets working directory to `/src/src/Garij.Web` and runs:
  `RUN dotnet publish "Garij.Web.csproj" -c Release -o /app/publish --no-restore`.
  This builds the application in Release mode (optimized code) and outputs the compiled binary files and web assets into `/app/publish`. The `--no-restore` flag skips re-downloading packages because they were already restored in Step 2.

##### Stage 2: Runtime Image (`FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final`)
- **Base Image:** `mcr.microsoft.com/dotnet/aspnet:10.0`. This is Microsoft's slim runtime image. It includes only the ASP.NET Core web server and libraries needed to run the app, making the final Docker image much smaller and more secure.
- **Working Directory:** `/app`.
- **Step 1 — Directory Permissions:** `RUN mkdir -p /app/data && chown -R app:app /app` creates a directory for database files and assigns full ownership of `/app` to the built-in system user named `app`.
- **Step 2 — Least-Privilege Execution:** `USER app` switches execution away from `root` to the restricted `app` user for container security.
- **Step 3 — Copying Binaries:** `COPY --from=build --chown=app:app /app/publish .` transfers only the published binaries from Stage 1 into the runtime image, setting `app` ownership so the web server can read and execute them.
- **Step 4 — Environment Configuration:**
  - `ENV ASPNETCORE_URLS=http://+:8080`: Tells the server to listen on port 8080 across all incoming addresses.
  - `ENV ASPNETCORE_ENVIRONMENT=Production`: Instructs ASP.NET Core to run in production mode (activating HSTS and production error views).
- **Step 5 — Exposed Port:** `EXPOSE 8080` documents that the container listens on port 8080.
- **Step 6 — Startup Command:** `ENTRYPOINT ["dotnet", "Garij.Web.dll"]` executes the compiled web server when the container starts.

#### How the App Picks Up Render's Port
On Render, the platform allocates an internal port for the container and provides that number through an environment variable named `PORT` (for example, `PORT=10000`).

In `src/Garij.Web/Program.cs` (lines 12–16), the application explicitly reads this variable:
```csharp
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://*:{port}");
}
```

**Step-by-step behavior:**
1. When the program starts, it asks the operating system if an environment variable named `PORT` exists.
2. If `PORT` is found (as on Render), `builder.WebHost.UseUrls($"http://*:{port}")` directs the Kestrel web server to bind to that exact port on all network interfaces (`*`).
3. If `PORT` is not set (such as in local Docker testing), the code skips this block and Kestrel falls back to `ENV ASPNETCORE_URLS=http://+:8080` from the Dockerfile.

---

### 3. Database in Production

#### Active Database Provider
- **Live Provider:** **SQLite** running inside the container.
- **Configured Connection String:** In `appsettings.json`, the connection string is configured as:
  `"ConnectionStrings": { "DefaultConnection": "Data Source=Garij.db" }`
- **File Location:** Resolves to `/app/Garij.db` (inside the container's working directory).
- **Third-Party / Cloud Providers:** **NOT ADDED**. The package `Npgsql` (PostgreSQL) is NOT present in any `.csproj` file. No cloud PostgreSQL or external SQL Server is configured in the code.

#### Database Provider Selection Code
In `src/Garij.Infrastructure/DependencyInjection.cs` (lines 13–30):
```csharp
var connectionString = configuration.GetConnectionString("DefaultConnection");

services.AddDbContext<GarijDbContext>(options =>
{
    if (string.IsNullOrEmpty(connectionString) || connectionString.Contains("Data Source=") || connectionString.Contains(".db"))
    {
        options.UseSqlite(string.IsNullOrEmpty(connectionString) ? "Data Source=Garij.db" : connectionString);
    }
    else if (!OperatingSystem.IsWindows() && (connectionString.Contains("(localdb)") || connectionString.Contains("Server=localhost") || connectionString.Contains("Server=127.0.0.1")))
    {
        // Fallback to SQLite on non-Windows platforms if local SQL Server / LocalDB is specified in dev environment
        options.UseSqlite("Data Source=Garij.db");
    }
    else
    {
        options.UseSqlServer(connectionString);
    }
});
```

#### Package References in `src/Garij.Infrastructure/Garij.Infrastructure.csproj`
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.11" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.11">
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.11" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.11" />
  <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.11" />
  <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.11" />
</ItemGroup>
```

#### EF Core Migrations
No migrations were created or regenerated for the deployment. The existing migrations in `src/Garij.Infrastructure/Migrations/` are:
1. `20260826093448_InitialCreate.cs`
2. `20260909141333_AddProjectPurchases.cs`
3. `20260909141354_AddPartConcurrencyToken.cs`
4. `20260909143542_AddDataIntegrityCheckConstraints.cs`

#### How the Schema is Created on Deploy
- Schema creation does **not** use `context.Database.MigrateAsync()`, nor does it use a pre-deploy shell script.
- Instead, the schema is created on startup inside `DbSeeder.cs` via:
  ```csharp
  await context.Database.EnsureCreatedAsync();
  ```
  in `src/Garij.Infrastructure/SeedData/DbSeeder.cs` (line 23), invoked by `src/Garij.Web/Program.cs` (line 96):
  ```csharp
  await DbSeeder.SeedAsync(app.Services);
  ```

#### Does the Demo Seeder Run in Production?
**YES.** There is no conditional check (such as `if (app.Environment.IsDevelopment())`) surrounding `await DbSeeder.SeedAsync(app.Services);`. It executes unconditionally on every application startup, including production container launches on Render.

#### Data Persistence and Ephemeral Filesystem Behavior
- **Data File Path:** `/app/Garij.db`
- **Does Data Survive a Redeploy or Restart?** **NO.**
  Render Web Services run inside an ephemeral Linux container. When a new deployment occurs, the web service crashes/restarts, or the container spins down due to inactivity on the Free plan, Render destroys the old container and instantiates a brand new container from the Docker image. Because no Render Persistent Disk is attached and configured in the connection string, **any new records, customer files, invoices, or transactions created during a session are wiped out**. Upon the next boot, `DbSeeder.SeedAsync()` re-creates the database from scratch and re-populates the initial seed dataset.

---

### 4. Configuration

#### Complete List of Configuration Keys Read by the App

| JSON Configuration Key | Render Environment Variable Name | Purpose | Shape / Example Value | Required? |
| :--- | :--- | :--- | :--- | :--- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Database connection string for EF Core | `Data Source=Garij.db` *(SQLite)* or `Server=<host>;Database=<db>;User Id=<user>;Password=<password>;TrustServerCertificate=True;` *(SQL Server)* | **No** (Defaults in code to `Data Source=Garij.db`) |
| `GeminiSettings:ApiKey` | `GeminiSettings__ApiKey` | Google Gemini API key for AI diagnostic suggestions | Secret API string (e.g., `AIzaSy...`) | **No** (App gracefully handles empty key; shows notice on intake form) |
| `GeminiSettings:Model` | `GeminiSettings__Model` | Gemini AI model identifier | `gemini-3.6-flash` | **No** (Defaults in config) |
| `GeminiSettings:BaseUrl` | `GeminiSettings__BaseUrl` | Google Generative Language REST base endpoint | `https://generativelanguage.googleapis.com/v1beta/` | **No** (Defaults in config) |
| `BillingSettings:TaxRatePercent` | `BillingSettings__TaxRatePercent` | Invoice tax percentage rate | `15` | **No** (Defaults to 15) |
| `LicenseSettings:Price` | `LicenseSettings__Price` | Price for workshop buyout license | `499.00` | **No** (Defaults to 499.00) |
| `LicenseSettings:Currency` | `LicenseSettings__Currency` | Currency for buyout license checkout | `USD` | **No** (Defaults to USD) |
| `LicenseSettings:ProductName` | `LicenseSettings__ProductName` | Descriptive name of workshop software license | `Garij Intelligent Vehicle Workshop — Lifetime License` | **No** (Defaults in config) |
| `Logging:LogLevel:Default` | `Logging__LogLevel__Default` | Root logger minimum severity level | `Information` | **No** (Defaults in config) |
| `Logging:LogLevel:Microsoft.AspNetCore` | `Logging__LogLevel__Microsoft__AspNetCore` | ASP.NET Core framework log level | `Warning` | **No** (Defaults in config) |
| `AllowedHosts` | `AllowedHosts` | Allowed HTTP Host headers | `*` | **No** (Defaults to `*`) |
| *Runtime Hosting* | `PORT` | Dynamic TCP port assigned by Render | e.g. `10000` | **Auto-injected by Render** |
| *Runtime Hosting* | `ASPNETCORE_URLS` | Fallback Kestrel binding URL | `http://+:8080` | **Set in Dockerfile** |
| *Runtime Hosting* | `ASPNETCORE_ENVIRONMENT` | Application execution environment | `Production` | **Set in Dockerfile** |

#### Status of `appsettings.Production.json`
- `appsettings.Production.json`: **NOT FOUND**.
- The deployed application relies entirely on `src/Garij.Web/appsettings.json` and container environment variables.

#### Verifiable Content of `src/Garij.Web/appsettings.json` (Masked)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=Garij.db"
  },
  "BillingSettings": {
    "TaxRatePercent": 15
  },
  "LicenseSettings": {
    "Price": 499.00,
    "Currency": "USD",
    "ProductName": "Garij Intelligent Vehicle Workshop — Lifetime License"
  },
  "GeminiSettings": {
    "ApiKey": "",
    "Model": "gemini-3.6-flash",
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

---

### 5. Production Code Changes

| Feature / Pattern | Present in Fork? | Exact Code / Implementation Details |
| :--- | :--- | :--- |
| **UseForwardedHeaders / ForwardedHeadersOptions** | **YES** | Configured in `src/Garij.Web/Program.cs` (lines 19–24 and 64):<br>```csharp<br>builder.Services.Configure<ForwardedHeadersOptions>(options =><br>{<br>    options.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor \| Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;<br>    options.KnownIPNetworks.Clear();<br>    options.KnownProxies.Clear();<br>});<br>...<br>app.UseForwardedHeaders();<br>```<br>*Purpose:* Render terminates TLS at its edge proxy and forwards requests as plain HTTP to the container. Clearing `KnownIPNetworks` and `KnownProxies` enables ASP.NET Core to trust Render's proxy headers and recognize requests as HTTPS. |
| **UseHttpsRedirection** | **YES** | Configured in `src/Garij.Web/Program.cs` (line 82):<br>```csharp<br>app.UseHttpsRedirection();<br>```<br>*Behavior:* Because `app.UseForwardedHeaders()` is placed before redirection, the app accurately sees that the client connected over HTTPS, preventing redirect loops. |
| **HSTS (HTTP Strict Transport Security)** | **YES** | Configured in `src/Garij.Web/Program.cs` (lines 77–80):<br>```csharp<br>if (!app.Environment.IsDevelopment())<br>{<br>    app.UseHsts();<br>}<br>```<br>*Verification:* Verified on `https://garij.onrender.com/` returning header `strict-transport-security: max-age=2592000`. |
| **Data Protection Key Persistence** | **NOT FOUND** | No persistent key store (`PersistKeysToFileSystem`, `PersistKeysToDbContext`, or Redis) is registered. ASP.NET Core stores keys in an ephemeral directory (`/home/app/.aspnet/DataProtection-Keys`). Upon container restart or redeploy, existing cookies/anti-forgery tokens become invalid, logging users out. |
| **Health Check Endpoint** | **NOT FOUND** | Neither `builder.Services.AddHealthChecks()` nor `app.MapHealthChecks("/health")` is defined in `Program.cs`. Querying `https://garij.onrender.com/health` returns `HTTP 404 Not Found`. |
| **Logging Changes** | **Standard** | Standard ASP.NET Core console logging. No third-party log forwarder (like Serilog or Datadog) was added. Render collects console output directly from stdout/stderr. |
| **CORS (Cross-Origin Resource Sharing)** | **NOT FOUND** | `AddCors()` and `UseCors()` are not configured. The application operates strictly same-origin. |
| **Static File Changes** | **YES** | Uses both `app.UseStaticFiles()` and `app.MapStaticAssets()` (.NET 10 optimized static asset mapping):<br>```csharp<br>app.UseStaticFiles();<br>app.MapStaticAssets();<br>app.MapControllerRoute(<br>    name: "default",<br>    pattern: "{controller=Home}/{action=Index}/{id?}")<br>    .WithStaticAssets();<br>``` |

#### Workarounds Added Specifically for Render
1. **Dynamic Port Binding:**
   ```csharp
   var port = Environment.GetEnvironmentVariable("PORT");
   if (!string.IsNullOrEmpty(port))
   {
       builder.WebHost.UseUrls($"http://*:{port}");
   }
   ```
   Render routes web traffic to a variable internal port defined in `PORT`. This ensures Kestrel listens on `0.0.0.0` on Render's required port.
2. **Reverse Proxy Header Trust:**
   ```csharp
   options.KnownIPNetworks.Clear();
   options.KnownProxies.Clear();
   ```
   By default, ASP.NET Core rejects `X-Forwarded-*` headers unless coming from loopback (`127.0.0.1`). Because Render's proxy routes traffic through private 10.x.x.x network IPs, clearing these collections allows the forwarded headers to be accepted.
3. **Container Directory Permissions for SQLite:**
   In `Dockerfile`:
   ```dockerfile
   RUN mkdir -p /app/data && chown -R app:app /app
   USER app
   ```
   Ensures the non-root `app` user has write permissions to create and update the SQLite `.db`, `.db-shm`, and `.db-wal` files in `/app`.

---

### 6. Local Reproduction

To build and run the exact production container locally:

#### Step 1: Build the Production Image
Open a terminal in the root directory of the repository:
```bash
docker build -t garij:production .
```

#### Step 2: Run the Production Container
Run the container locally on port 8080:
```bash
docker run -d \
  --name garij-local \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e GeminiSettings__ApiKey="<YOUR_GEMINI_API_KEY>" \
  garij:production
```

*(Optional: Simulate Render's dynamic `PORT` assignment on port 10000)*:
```bash
docker run -d \
  --name garij-render-sim \
  -p 10000:10000 \
  -e PORT=10000 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e GeminiSettings__ApiKey="<YOUR_GEMINI_API_KEY>" \
  garij:production
```

#### Step 3: Verify the Running Container
Confirm the container is running and healthy:
```bash
# Check container status
docker ps --filter "name=garij-local"

# View startup and DbSeeder logs
docker logs garij-local

# Check HTTP response headers
curl -I http://localhost:8080/

# Confirm home page HTML renders
curl -s http://localhost:8080/ | grep -i "Garij"
```

Open a web browser and visit: `http://localhost:8080/`

---

### 7. Known Issues on the Live Site

1. **Cold Starts on the Free Plan:**
   Because the service is hosted on Render's Free tier, the web container is stopped after 15 minutes of inactivity. When a new visitor accesses `https://garij.onrender.com/`, Render must boot a new container from scratch. This cold start causes an initial page load delay of approximately **30 to 60 seconds**.
2. **Data Reset on Container Restart / Redeployment:**
   The SQLite database file is stored inside the container filesystem at `/app/Garij.db`. Because the free plan does not include a persistent disk, **any data created during user sessions is lost whenever the service restarts, sleeps, or is redeployed**. The application automatically resets to the initial demo seed data.
3. **Session and Cookie Invalidation:**
   Data Protection encryption keys are generated in temporary container storage. When Render restarts the container, previous keys are lost. Any users who were logged in will have their cookies invalidated and will be redirected to the login screen.
4. **Publicly Exposed Default Credentials in Production:**
   Because `DbSeeder.cs` runs on every startup in production, standard well-known demo accounts are always active:
   - **Administrator:** `admin@garij.com` / `Admin@12345`
   - **Front Desk:** `frontdesk@garij.com` / `Staff@12345`
   - **Mechanic:** `mechanic@garij.com` / `Mechanic@12345`
   - **Demo License Key:** `GRJ-DEMO-2026-KEY`
5. **AI Smart Intake Fallback Without API Key:**
   If `GeminiSettings__ApiKey` is not configured in the Render Dashboard environment variables, calls to Google Gemini return an advisory warning on the job intake page stating that AI diagnostic assistance is offline. Manual service job creation continues to work normally.
6. **SQLite File-Level Locking Under Concurrent Traffic:**
   SQLite uses file locking for writes. If multiple users attempt to perform simultaneous invoice payments or status updates, the server may experience database lock contention (`busy_timeout`), unlike a dedicated database server like PostgreSQL.
7. **`/health` Route Returns 404:**
   ASP.NET Core health checks are not registered in `Program.cs`. If Render's Health Check Path setting is pointed to `/health`, deployments will register as failed. The health check path must be configured to `/` or left empty.

---

## PART 2 — FROM THE RENDER DASHBOARD (Human Checklist)

> **Instructions for the Engineer / Author:**
> The following parameters cannot be inspected directly from the repository code and must be retrieved from the Render Management Dashboard at [dashboard.render.com](https://dashboard.render.com). Fill in the blanks below to complete the report chapter.

- [ ] **Service Name:** `_________________________` *(e.g., garij)*
- [ ] **Service Type:** `_________________________` *(e.g., Web Service / Docker)*
- [ ] **Render Region:** `_________________________` *(e.g., Oregon (US West), Frankfurt (EU Central), Singapore)*
- [ ] **Instance Plan:** `_________________________` *(e.g., Free, Starter ($7/mo), Standard)*
- [ ] **Connected Repository:** `https://github.com/SHOEBILL04/Garij.git`
- [ ] **Branch:** `main`
- [ ] **Auto-Deploy on Push:** `[ ] Yes  [ ] No`
- [ ] **Runtime:** `Docker`
- [ ] **Dockerfile Path:** `Dockerfile`
- [ ] **Docker Context Directory:** `.` *(Root)*
- [ ] **Build Command / Start Command:** `N/A (Managed by Dockerfile)`
- [ ] **Configured Environment Variables (Names Only):**
  - [ ] `PORT` *(Render System Managed)*
  - [ ] `ASPNETCORE_ENVIRONMENT` *(Value: Production)*
  - [ ] `GeminiSettings__ApiKey` *(Configured: [ ] Yes  [ ] No)*
  - [ ] `ConnectionStrings__DefaultConnection` *(Configured: [ ] Yes  [ ] No)*
  - [ ] Other: `____________________________________`
- [ ] **Persistent Disk Attached:** `[ ] Yes  [ ] No`
  - Mount Path (if yes): `_________________________` *(e.g., /app/data)*
  - Disk Size (if yes): `_________________________` *(e.g., 1 GB)*
- [ ] **Database Service Attached:** `[ ] None (SQLite container)  [ ] Render PostgreSQL  [ ] External SQL Server`
  - Database Plan / Host (if applicable): `_________________________`
- [ ] **Health Check Path Configured:** `_________________________` *(e.g., / or blank)*
- [ ] **Custom Domain Configured:** `[ ] Yes  [ ] No` *(Live domain: https://garij.onrender.com/)*
- [ ] **Date of First Successful Deploy:** `_________________________`
- [ ] **Date / Commit of Latest Deploy:** `_________________________` *(Commit b2d6371)*
- [ ] **Typical Build Time on Render:** `_________________________` *(e.g., 2m 15s)*
- [ ] **Cold-Start Wakeup Time (Free Plan):** `_________________________` *(e.g., 45s)*
- [ ] **Deploy Failures Encountered & Remediation:**
  - *Failure 1:* `_________________________________________________________________`
  - *Fix 1:* `_____________________________________________________________________`

---

## PART 3 — SCREENSHOT LIST

Capture the following screenshots to illustrate the deployment chapter in the final report. Save each image to the `screenshots/` directory using the designated filenames.

| Figure Filename | Subject / Target View | Description & Verification Elements |
| :--- | :--- | :--- |
| **`fig1_R1_render_service_overview.png`** | Render Service Overview Page | Full view of the Render service dashboard showing the service name (`garij`), status pill (**"Live"** in green), region, plan type, and public URL (`https://garij.onrender.com/`). |
| **`fig1_R2_render_environment.png`** | Render Environment Tab | Screenshot of the Environment tab showing configured environment variable names (`PORT`, `GeminiSettings__ApiKey`, etc.) with all secret values masked/hidden. |
| **`fig1_R3_render_deploy_log.png`** | Deployment Build & Start Log | Terminal window showing the successful Docker multi-stage build log, NuGet restore, publish step, and terminating in the line **"Your service is live 🎉"**. |
| **`fig1_R4_render_settings.png`** | Build & Deploy Settings | View of the Settings tab displaying the Git repository URL (`SHOEBILL04/Garij`), branch (`main`), build runtime (`Docker`), Dockerfile path (`Dockerfile`), and Auto-Deploy toggle. |
| **`fig1_R5_live_site.png`** | Live Website in Browser | High-resolution capture of `https://garij.onrender.com/` loaded in Chrome/Edge, showing the navigation bar, workshop branding, theme switch, and live landing hero section. |
| **`fig1_R6_docker_local_run.png`** | Local Docker Reproduction Terminal | Terminal screen capturing execution of `docker build -t garij:production .` followed by `docker run` and `curl -I http://localhost:8080/` demonstrating HTTP 200/302 response. |
| **`fig1_R7_live_login_seed.png`** | Production Staff Login Screen | Web browser view of `https://garij.onrender.com/Account/Login` demonstrating the authentication page and pre-seeded demo login capabilities. |
| **`fig1_R8_live_smart_intake.png`** | Smart Intake AI Assistant View | Interface view of the job intake page showing the Google Gemini diagnostic recommendations advisory partial. |

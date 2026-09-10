# 🧠 AI & Developer Context — Garij Project

> **IMPORTANT INSTRUCTION FOR ALL AI ASSISTANTS:**
> Whenever you start working on this repository or finish a task:
> 1. Read this `CONTEXT.md` file alongside `ROADMAP.md` to understand the current architecture, completed tasks, and team responsibilities.
> 2. Implement features on a dedicated feature branch (e.g., `feature/<task-name>`). Never commit directly to `main`.
> 3. After completing your work, **MUST UPDATE THIS `CONTEXT.md` FILE** with what was changed, any new decisions made, and the current state of the project.

---

## 📌 Project Overview
**Garij** is an Intelligent Vehicle Service Center Management System built with ASP.NET Core 10.0 MVC using a 3-layer architecture (Domain, Application, Infrastructure) + Presentation Layer (Web).

---

## 🛠️ Architecture & Tech Stack
- **Framework**: .NET 10.0 ASP.NET Core MVC
- **Architecture**: Layered Architecture (Clean separation of concerns)
  - `src/Garij.Domain`: Domain Entities, Enums, Exceptions (Zero dependencies).
  - `src/Garij.Application`: Service Interfaces, DTOs, Business Logic.
  - `src/Garij.Infrastructure`: EF Core `GarijDbContext`, Repositories, Identity, External APIs (Gemini).
  - `src/Garij.Web`: Controllers, Views, Middleware, Identity UI, Dependency Injection.
  - `tests/Garij.UnitTests`: Unit testing suite.
  - `tests/Garij.IntegrationTests`: Integration testing suite.
  - `tests/Garij.Tests`: General testing project.
- **Database**: **Microsoft SQL Server (MS SQL)**:
  - **Standard Database**: Microsoft SQL Server / LocalDB (`Server=...;Database=GarijDb;...`).
  - **Fallback Development Support**: SQLite (`Data Source=Garij.db`) for zero-config Linux development environments when specified.
- **Authentication & Security**: ASP.NET Core Identity with role-based authorization (`Admin`, `Receptionist`, `Mechanic`, `Customer`).
- **Global Exception Middleware**: `GlobalExceptionMiddleware` catches uncaught exceptions, formats API/JSON responses for AJAX requests, and redirects to HTML error page for browser requests.

---

## 👥 Team Responsibility Matrix & Vertical Slice Ownership (Aligned with ROADMAP.md)

| Developer | Owned Modules | Primary Files / Interfaces | Stage 1 Core Focus |
| :--- | :--- | :--- | :--- |
| **Samia Tabassum** | Customer & Vehicle, Public Status Lookup | `CustomerVehicleService`, `CustomerController`, `VehicleController`, `StatusLookupController` | Customer registration, vehicle lookup, license plate validation |
| **Rakibul Islam Emon** | Service Jobs, Mechanic Assignment | `ServiceJobService`, `ServiceJobController`, `MechanicController` | Service job creation, mechanic assignment, Service Catalog seeding (`GRJ-2026-XXXX`) |
| **Rubaiat Ar Rabib** | Parts & Inventory, Notifications | `PartsInventoryService`, `NotificationService`, `PartsController`, `NotificationController` | Part stock management, 15-20 part seeding, reorder level alerts |
| **Aftab Ahmed Fahim** | Billing & Invoicing, Reporting, Intelligence | `BillingService`, `ReportingService`, `IntelligenceService`, `AccountController`, `DashboardController` | Auth/Identity setup, role navigation (`_Layout.cshtml`), Invoice generation skeleton |

---

## 📋 Roadmap & GitHub Issues Breakdown

### Stage 0 — Blockers (Immediate Focus)
1. **Identity Model Consolidation**: Finalize single Identity User model or proper FK link to `AspNetUsers`.
2. **Customer → ServiceJob Link**: Reconcile direct link vs vehicle link.
3. **Default Route**: Update default route to `Dashboard` / `Account/Login`.
4. **Solution File Alignment**: Standardize on `Garij.sln` across build scripts and CI pipelines.
5. **Initial Migration & Seeding**: Generate initial EF Core migration targeting MS SQL Server.

### Stage 1 — Foundation & Core CRUD
- **Issue #1 (Samia)**: Implement `CustomerVehicleService` & CRUD Views (`CustomerController`, `VehicleController`).
- **Issue #2 (Emon)**: Implement `ServiceJobService` & Job Booking Reference Generation (`ServiceJobController`, `MechanicController`).
- **Issue #3 (Rubaiat)**: Implement `PartsInventoryService` & Stock Validation (`PartsController`).
- **Issue #4 (Aftab)**: Account Controller, Auth Guard, Dashboard Routing & Billing Skeleton (`AccountController`, `BillingService`).

### Stage 2 — Business Logic & Workflows
- **Issue #5 (Samia)**: Public Status Lookup Timeline View & Service History (`StatusLookupController`).
- **Issue #6 (Emon)**: Job Status State Machine & Diagnostic Log (`ValidateStatusTransition`).
- **Issue #7 (Rubaiat)**: Log Parts Used, Atomic Stock Decrement & Notification Approval Queue (`LogPartsUsed`).
- **Issue #8 (Aftab)**: Transactional Invoice Generation & Multi-Method Payment Recording (`GenerateInvoice`).

### Stage 3 — Intelligence, Reporting & Polish
- **Issue #9 (Samia)**: UI/UX Pass, Color-Coded Job Board Statuses & Mobile Optimization.
- **Issue #10 (Emon)**: Service-Due Vehicle Flagging & Advanced Job Board Filtering.
- **Issue #11 (Rubaiat)**: Stock Concurrent Decrement Unit Tests & Presentation Demo Dataset.
- **Issue #12 (Aftab)**: Smart Intake Gemini AI Assistant & Executive Reports (`IReportingService`).

---

## 📝 Recent Progress Log

### [2026-09-10] - Stage 3: Service-Due Vehicle Flagging & Advanced Job Board Filtering (Rakibul Islam Emon)
- **Vehicle Maintenance Predictions (FR-12)**:
  - Implemented `IIntelligenceService.FlagVehiclesDueForServiceAsync` and `PredictMaintenanceDueAsync` in `IntelligenceService.cs`.
  - Computes predictive maintenance cadences using each vehicle's completed service job history, calculating individual service cycle intervals or applying a standard 90-day baseline for single-service vehicles.
  - Generates rich projection details via `VehicleMaintenancePredictionDto`: last service date, days elapsed, forecast due date, days overdue/remaining, urgency level (`Overdue`, `Due Soon`), and recommended service action.
- **Front-Desk Dashboard Integration**:
  - Integrated `IIntelligenceService` into `DashboardController.cs` and `Views/Dashboard/Index.cshtml` via `FrontDeskDashboardViewModel`.
  - Prominently displays overdue and due-soon vehicles with customer contact information, urgency badges, and quick-action "Book Job" buttons pre-filling the vehicle.
- **Job Board Multi-Dimensional Filtering & Sorting**:
  - Added `GetFilteredServiceJobsAsync` to `IServiceJobService` and `ServiceJobService` supporting simultaneous filtering by status, mechanic, and date order (`date_desc`, `date_asc`, `status`, `plate`), alongside full-text search.
  - Enhanced touch-friendly `Views/Mechanic/JobBoard.cshtml` and `Views/ServiceJob/Index.cshtml` with comprehensive filter bars and state preservation across status transitions and diagnostic note updates.
- **Status State Machine Integration Testing**:
  - Authored comprehensive integration test suite `ServiceJobStatusTransitionIntegrationTests.cs` covering legal workflow advancement (`Requested` -> `InspectionPending` -> `CustomerApprovalNeeded` -> `InProgress` -> `Completed`), valid cancellations, terminal state locks, illegal state transitions (BR-007 rejections), job board filtering/sorting, and maintenance prediction forecasting.
  - 107/107 solution tests passing (100% pass rate).

### [2026-09-09] - Smart Intake Gemini AI Assistant & Executive Business Reports (Aftab Ahmed Fahim) - Issue #31
- **Executive Business Reports (FR-14, FR-15, FR-17)**:
  - **Monthly Revenue Report**: Implemented side-by-side comparison of accrual-basis Billed revenue (Net SubTotal and Gross TotalAmount excluding Refunded invoices) vs cash-basis Collected payments (`PaymentTransaction`), monthly aggregations, invoice count, and average ticket size.
  - **Part Consumption Report**: Ranked inventory usage spend by total cost (`QuantityUsed * PriceAtUsage`), anchored to job completion dates (with explicit UI disclosure of the date basis), and flagged low-stock items (`QuantityInStock <= ReorderLevel`).
  - **Mechanic Workload Report**: Aggregated ticket volumes (Active vs Completed) per mechanic broken down by `RoleInJob` (`Lead` vs `Assistant`), along with each technician's percentage share of completed shop jobs (with explicit UI disclosure that workload is measured in ticket volume).
  - **Reporting UI**: Implemented reusable date-range filter partial `_DateRangeFilterPartial.cshtml` with quick presets, Chart.js visual charts, summary cards, and clean `@media print` printability across all report views (`Revenue.cshtml`, `PartConsumption.cshtml`, `MechanicWorkload.cshtml`, `Index.cshtml`).
- **Gemini Smart Intake Assistant (FR-18, FR-20, FR-21)**:
  - **LLM Grounding & Server-Side JSON Schema**: Implemented `ILlmClient` and `GeminiClient` targeting native endpoint with server-side `responseSchema` and `x-goog-api-key` header (avoiding URL logging leakage). Grounded prompts on unfiltered database `ServiceCatalog` entries.
  - **Safety Gate & Audit Logging**: Validates every recommended `serviceCatalogId` against the database catalog, discards hallucinated/unverified IDs with UI warning counts, and logs every request/response into `AiRequestLogs` table with latency tracking.
  - **Advisory-Only Intake Integration**: Created `_IntakeAssistantPartial.cshtml` embedded into `Views/ServiceJob/Create.cshtml` (one permitted line). Confirmed suggestions are formatted and appended directly into `DiagnosticNotes` on the client side; never writes unconfirmed records to the database.
- **Decisions**:
  - Revenue reporting basis: collected revenue is an unfiltered cash intake ledger; billed revenue excludes currently-Refunded invoices; refunds shown as a separate KPI in neither total. Refunds have no timestamp, so revenue figures are point-in-time, not historically immutable.
  - Garij.Application references Garij.Infrastructure, inverting the intended dependency direction. Pre-existing baseline debt, not introduced here. Flagged so nobody builds on the assumption that Application is infrastructure-free.
- **Testing & Invariant Verification**:
  - Added unit test suites `ReportingServiceTests.cs` (revenue side-by-side, refunded exclusions, part consumption date fallbacks, mechanic workload shares, empty ranges) and `IntakeAssistantTests.cs` (hallucination discarding safety gate, missing API key graceful degradation).
  - 100% solution test pass rate (88/88 tests passing: 46 unit / 8 general / 34 integration).
  - Verified zero schema changes: no migrations were added and no entity, EF configuration or DbSet was modified; `has-pending-model-changes` still reports pending changes from Emon's unmigrated ProjectPurchase, unchanged from before this work.

### [2026-09-08] - Light Mode & Dark Mode System with Image-Free Minimalist Light Aesthetic (Rakibul Islam Emon)
- **Light & Dark Theme Engine**:
  - Implemented client-side instant theme switching engine with `theme-toggle.js`, persisting preference in `localStorage.getItem("garij_theme")` (`dark` or `light`).
  - Added synchronous theme initializer in `<head>` of [_LandingLayout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_LandingLayout.cshtml), [_Layout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_Layout.cshtml), and [_AuthLayout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_AuthLayout.cshtml) ensuring zero flash of unstyled content (FOUC).
  - Added interactive theme toggle buttons in the desktop sticky navbar, mobile offcanvas drawer, internal app layout, and auth pages.
- **Dark Mode Preservation**:
  - Maintained 100% of the existing dark cinematic automotive theme with glowing cyan/neon accents, hero factory artwork, dark cards, and tachometer animation.
- **Image-Free Minimalist Light Mode**:
  - Per specifications, all heavy content images, car graphics, and photographic backgrounds are completely hidden in light mode (`display: none !important;`).
  - Pure porcelain and white minimal surface palette (`#f8fafc`, `#ffffff`, `#f1f5f9`) with clean slate typography (`#0f172a`, `#1e293b`, `#475569`).
  - Accented with bright, vibrant theme colors: electric cyan (`#0284c7`, `#00b4d8`) and automotive blue (`#0369a1`).
  - Renovation section features an elegant 4-metric minimal stat grid ("25+ Years Reputation", "99.8% Diagnostic Accuracy", "10k+ Repairs Completed", "100% Genuine Parts").
  - Testimonials section features a clean minimal vector pill badge (`Verified Client Endorsements`).
  - Complete light mode styling across internal dashboard, tables, forms, modals, navigation dropdowns, and auth screens.
- **Testing & Verification**:
  - Added integration test `Pages_RenderThemeToggleAndInitializer_ForLightAndDarkMode` in [ProjectPurchaseIntegrationTests.cs](file:///home/rakibul/Projects/garij/tests/Garij.IntegrationTests/ProjectPurchaseIntegrationTests.cs).
  - 100% pass rate across the full solution (77/77 tests passing).

### [2026-09-08] - Custom Garage Naming & Top Navigation Branding (Rakibul Islam Emon)
- **Admin Garage Naming & Branding**:
  - Implemented the ability for the admin/workshop owner who purchased the project to give their garage a custom name or rename it anytime.
  - Added `GetWorkshopNameAsync` and `UpdateWorkshopNameAsync` to [IProjectPurchaseService.cs](file:///home/rakibul/Projects/garij/src/Garij.Application/Interfaces/IProjectPurchaseService.cs) and [ProjectPurchaseService.cs](file:///home/rakibul/Projects/garij/src/Garij.Application/Services/ProjectPurchaseService.cs).
  - Synchronizes custom garage name across the owner's license and all associated garage staff licenses (front desk, mechanics) so the entire team sees the custom garage name everywhere.
  - Implemented `AdminController.GarageSettings` (`GET` and `POST`) and dedicated view [Views/Admin/GarageSettings.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Admin/GarageSettings.cshtml) with interactive live top-bar preview as the user types.
  - Added "Garage Identity & Branding" quick-edit banner to the Admin Dashboard ([Views/Admin/Index.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Admin/Index.cshtml)).
- **Prominent Top Navigation Display**:
  - Updated main system layout [_Layout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_Layout.cshtml) to prominently display the custom garage name at the very top of every page with "POWERED BY GARIJ" badge.
  - Updated landing layout [_LandingLayout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_LandingLayout.cshtml) to display the customized garage name in the top navbar brand logo.
- **Testing & Verification**:
  - Added unit test `UpdateWorkshopName_And_GetWorkshopName_UpdatesSuccessfully` in [ProjectPurchaseServiceTests.cs](file:///home/rakibul/Projects/garij/tests/Garij.UnitTests/ProjectPurchaseServiceTests.cs).
  - Added integration test `Admin_CanRenameGarage_AndItShowsAtTheTop` in [ProjectPurchaseIntegrationTests.cs](file:///home/rakibul/Projects/garij/tests/Garij.IntegrationTests/ProjectPurchaseIntegrationTests.cs).
  - 100% test pass rate across the solution (76/76 tests passing).

### [2026-09-08] - Buyer Role Selection & Staff Account Management (Rakibul Islam Emon)
- **Buyer Role Selection at Checkout**:
  - Extended [CreateProjectPurchaseDto.cs](file:///home/rakibul/Projects/garij/src/Garij.Application/DTOs/CreateProjectPurchaseDto.cs) with `AccountRole` property (defaults to `Admin` / Workshop Owner).
  - Updated [PurchaseController.cs](file:///home/rakibul/Projects/garij/src/Garij.Web/Controllers/PurchaseController.cs) so buyers purchasing the project can choose whether their account is created/assigned as `Admin`, `FrontDesk`, or `Mechanic`.
  - Updated [Views/Purchase/Index.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Purchase/Index.cshtml) with an "Assign My Account Role" dropdown selector.
- **Admin Staff User Creation & Role Assignment**:
  - Created [CreateStaffUserViewModel.cs](file:///home/rakibul/Projects/garij/src/Garij.Web/Models/CreateStaffUserViewModel.cs) and [Views/Admin/CreateUser.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Admin/CreateUser.cshtml) for workshop owners to create staff accounts with Full Name, Email, Phone Number, Role (`FrontDesk`, `Mechanic`, `Admin`), and Password.
  - Implemented `AdminController.CreateUser` (`GET` and `POST`) which registers the `IdentityUser`, assigns the role in ASP.NET Core Identity, creates a `User` in `_context.StaffUsers`, and grants staff license access so staff members do not need to purchase an individual license.
  - Updated [Views/Admin/ManageUsers.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Admin/ManageUsers.cshtml) with modern dark automotive UI table displaying staff profiles and direct "+ Create Staff Account" navigation.
  - Enhanced [Views/Admin/ManageRoles.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Admin/ManageRoles.cshtml) to allow administrators to reassign staff roles dynamically.
- **Jobs Section — Add Garage Employee Option**:
  - Implemented `ServiceJobController.AddEmployee` (`GET` and `POST`) allowing workshop managers and receptionists to register garage employees directly from the Jobs workflow.
  - Form allows entering Full Name, Email (Username), Phone Number, Role (`Mechanic`, `FrontDesk`, `Admin`), and Password.
  - Automatically covers employee accounts under the workshop's lifetime license with zero extra charges.
  - Added dedicated view [Views/ServiceJob/AddEmployee.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/ServiceJob/AddEmployee.cshtml).
  - Added "+ Add Employee" button to the Service Jobs page ([Views/ServiceJob/Index.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/ServiceJob/Index.cshtml)), Mechanic Roster ([Views/Mechanic/Index.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Mechanic/Index.cshtml)), Job Board ([Views/Mechanic/JobBoard.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Mechanic/JobBoard.cshtml)), and in the primary navbar "Jobs" dropdown menu ([Views/Shared/_Layout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_Layout.cshtml)).
- **Testing & Verification**:
  - Added integration tests to [ProjectPurchaseIntegrationTests.cs](file:///home/rakibul/Projects/garij/tests/Garij.IntegrationTests/ProjectPurchaseIntegrationTests.cs):
    1. `Admin_CanCreateStaffAccount_WithEmailPasswordAndRole_AndStaffCanLogin`: Verifies admin creates a mechanic staff account with email & password, and that mechanic can log in and view the mechanic job board.
    2. `PurchaseCheckout_WithMechanicRole_AssignsMechanicRoleAndGrantsAccess`: Verifies a buyer selecting the `Mechanic` role during checkout receives that role and can immediately log in and access the Job Board.
    3. `LicensedUser_DoesNotSeePricingInNavbar_OnLandingPage`: Verifies that once a user purchases the project or is licensed, the `PRICING` navigation menu and `BUY PROJECT` call-to-action button are completely hidden from the desktop and mobile navigation bars.
    4. `Jobs_CanAddEmployee_WithEmailPasswordAndRole_AndEmployeeCanLogin`: Verifies adding an employee directly from the Jobs workflow, assigning their role with email and password, and verifying immediate login and dashboard access without requiring a license purchase.
  - Full test suite passing at 100% (74/74 tests passing).
- **Navbar Dynamic Visibility Polish**:
  - Updated [_LandingLayout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_LandingLayout.cshtml) and [_Layout.cshtml](file:///home/rakibul/Projects/garij/src/Garij.Web/Views/Shared/_Layout.cshtml) to conditionally hide the `PRICING` menu link and `BUY PROJECT` button when a user has bought the project (or is an Admin), showing a neat `LICENSED` badge instead.

### [2026-09-07] - One-Time Payment & Lifetime Project License System (Rakibul Islam Emon)
- **One-Time Buyout & License Model**: Implemented a complete software license purchase and activation system allowing garage owners to buy the project once ($499.00 USD, zero recurring subscriptions) to unlock full access to use the platform.
- **Domain Layer (`Garij.Domain`)**: Added `ProjectPurchase.cs` entity and `LicenseStatus.cs` enum tracking cryptographically unique license keys (`GRJ-LIC-XXXX-XXXX-XXXX`), buyer details, transaction references, amount, and active states.
- **Infrastructure Layer (`Garij.Infrastructure`)**: Added `ProjectPurchaseConfiguration`, `DbSet<ProjectPurchase>` to `GarijDbContext`, `IProjectPurchaseRepository`, and `ProjectPurchaseRepository`. Updated `DbSeeder.cs` to seed default lifetime licenses for default demo workshop accounts (`admin@garij.com`, `frontdesk@garij.com`, `mechanic@garij.com`) and a reusable demo activation key `GRJ-DEMO-2026-KEY`.
- **Application Layer (`Garij.Application`)**: Added `LicenseSettings.cs` (options pattern), DTOs (`ProjectPurchaseDto`, `CreateProjectPurchaseDto`, `PurchaseResultDto`), `IProjectPurchaseService`, and `ProjectPurchaseService` handling payment validation, cryptographic key generation, user licensing checks, and key activation.
- **Presentation & Web Layer (`Garij.Web`)**:
  - `PurchaseController`: Implemented `Index` (pricing & checkout), `Checkout` (payment submission & auto-registration), `Success` (order receipt & digital license certificate), `Status` (license overview), and `Activate` (direct key redemption).
  - `RequireProjectLicenseAttribute`: Global action filter protecting internal workshop features (`Dashboard`, `Customer`, `Vehicle`, `ServiceJob`, `Mechanic`, `Parts`, `Billing`, `Report`), redirecting unlicensed users to `/Purchase` while allowing public pages (`/`, `/About`, `/Services`, `/Testimonials`, `/Pricing`, `/Account/*`, `/Purchase/*`).
  - Dedicated Public Views: Created dedicated responsive pages `Views/Home/About.cshtml`, `Views/Home/Services.cshtml`, `Views/Home/Testimonials.cshtml`, and `Views/Home/Pricing.cshtml` so public presentation sections remain completely open and accessible to all users whether logged out or logged in without a license.
  - Locked Dashboard Indicators: In `_LandingLayout.cshtml` and `_Layout.cshtml`, unlicensed logged-in users are presented with a clear `Dashboard (Locked)` badge and guidance directing them to the purchase page, while public presentation navigation links remain fully accessible.
  - Enhanced Checkout & Landing UI: Added instant 1-click test approval and demo key autofill to `Views/Purchase/Index.cshtml`, plus fixed anchor scroll margins in `landing.css` and hash auto-scroll handling in `landing.js`.
- **Testing & Verification**: Created `ProjectPurchaseServiceTests` (6 unit tests) and `ProjectPurchaseIntegrationTests` (12 integration tests covering anonymous and unlicensed authenticated public access). Solution tests passing at 100% (70/70 tests passing cleanly).

### [2026-08-29] - Landing Page UI Redesign & High-Tech Automotive Overhaul (Aftab Ahmed Fahim)
- **Public Landing Page (HomeController & _LandingLayout)**: Created `HomeController` (`[AllowAnonymous]`) mapped to the default route `/`, with dedicated full-bleed dark automotive layout `_LandingLayout.cshtml` and view `Views/Home/Index.cshtml`.
- **Visual Design & Aesthetics (Demo Mockup Alignment)**:
  - Top contact info bar with 24/7 phone (`0-800-123-4567`), email (`Carrepair@example.com`), and Miami location (`9332 Bernier Dam, Miami, USA`).
  - Dark cinematic hero section featuring vintage muscle car in atmospheric smoke, winged crossed spark-plug repair badge ("REPAIR SERVICE 1983 / TUNING"), uppercase Montserrat headline "CREATIVE & PROFESSIONAL", and glowing "READ MORE" button.
  - "YOU NEED RENOVATION?" section with geometric blue border framing, descriptive copy, 3D tuned sports car with glowing blue neon wheel rims and underglow, and 3 feature cards (25 Years Reputation, Full Integrity, Quick & Efficient).
  - Testimonials section with winged emblem and 3-column client review cards (Bill Alvarado, Alberta Wilson, Zachary Fernandez).
  - Modern automotive footer with workshop operating hours, quick links, and copyright.
- **Interactive Animations & Preloader**:
  - Tachometer/Speedometer revving loading animation (`#page-preloader`) on initial page load.
  - IntersectionObserver scroll reveal animations (`reveal-left`, `reveal-right`, `reveal-up`) across cards and sections.
  - Glowing hover effects with animated expanding underlines on navigation links and dedicated neon buttons for **Status Lookup**, **Register**, and **Login**.
- **Typography & Asset Organization**:
  - Configured Montserrat typography (Regular 400, Italic 400i, Semi-Bold 600, Bold 700/800) with both local `@font-face` support in `wwwroot/fonts/` and Google Fonts fallback.
  - Structured `wwwroot/images/` for user assets: `hero-car.png`, `renovation-car.png`, `repair-badge.svg`, and `testimonial-badge.svg`.
- **Testing & Verification**: Verified build (`dotnet build Garij.sln`), 100% unit and integration test pass rate (52/52 tests in `dotnet test Garij.sln`), and verified in browser.

### [2026-08-29] - Stage 2 Business Logic & Mechanic Workflows (Rakibul Islam Emon)
- **Job Status State Machine (FR-7)**: Implemented strict status state machine transition validation in `ServiceJobService.ValidateStatusTransition`. Allowed flow: `Requested` &rarr; `InspectionPending` &rarr; `CustomerApprovalNeeded` &rarr; `InProgress` &rarr; `Completed`. `Cancelled` is accessible from any active non-completed state. Invalid transitions trigger `BusinessRuleException` ("BR-007").
- **Logged Parts Completion Pre-Condition (FR-8)**: Implemented hard business rule in `ServiceJobService` preventing jobs from transitioning to `Completed` status unless at least one part used is recorded in `JobPartsUsed`. Rejections throw `BusinessRuleException` ("BR-008").
- **Lead Mechanic Uniqueness Constraint (FR-3)**: Enforced single Lead mechanic constraint in `AssignMechanicAsync`. Attempting to assign a second lead technician throws `BusinessRuleException` ("BR-003").
- **Touch-Friendly Mechanic Job Board (FR-10)**: Created `MechanicController.JobBoard` and touch-optimized view (`Views/Mechanic/JobBoard.cshtml`) featuring responsive cards, mechanic filter dropdown, inline diagnostic notes editor, log parts quick link, and status action buttons.
- **Repository & Service Contracts**: Extended `IServiceJobRepository`, `ServiceJobRepository`, `IServiceJobService`, and `ServiceJobService` with `GetJobsByMechanicAsync(int mechanicUserId)` and `SaveDiagnosticNotesAsync(int serviceJobId, string notes)`.
- **Navigation Layout Update**: Added "Job Board" link to top navigation bar in `_Layout.cshtml` for authenticated mechanics, front desk staff, and admins.
- **Comprehensive Unit Testing**: Expanded `ServiceJobServiceTests.cs` with test cases for valid transitions, invalid transition rejections, parts completion guards, and lead mechanic uniqueness. Verified all 48 solution unit/integration tests pass (`dotnet test`).
- **Git Branching**: Committed and pushed changes to remote branch `feature/business-logic-mechanic-workflows` using conventional commits.

### [2026-08-26] - Customer Direct Link Reconciliation, Default Route Update & Solution Cleanup
- **Customer → ServiceJob Direct Link**: Added `CustomerId` FK and `Customer` navigation property to `ServiceJob.cs` and `ServiceJobs` collection to `Customer.cs` matching report ERD. Configured `FK_ServiceJobs_Customers_CustomerId` in `ServiceJobConfiguration`.
- **Default Route Landing**: Updated default route in `Program.cs` to `Dashboard` (`{controller=Dashboard}/{action=Index}`), routing unauthenticated users to `/Account/Login`.
- **Solution Standardization**: Removed duplicate `Garij.slnx` file to standardize build tools exclusively on `Garij.sln`.
- **EF Core Migration & Verification**: Re-generated MS SQL EF Core initial migration `20260826093448_InitialCreate`. All unit and integration tests pass cleanly (100% success).

### [2026-08-26] - Identity Model Structure Resolution & Authorization Module Setup
- **Identity Model Consolidation**: Finalized Foreign Key (FK) Link between domain `User` (`StaffUsers`) and ASP.NET Core Identity (`AspNetUsers`). Configured `FK_StaffUsers_AspNetUsers_IdentityUserId` in `UserConfiguration` with cascade delete and index.
- **Entity Cleanup**: Removed redundant `ApplicationUser` entity and configuration.
- **Seeding Enhancement**: Updated `DbSeeder` to automatically seed `Admin`, `FrontDesk`, and `Mechanic` users in both ASP.NET Identity and `StaffUsers` table with role assignments.
- **Authorization Modules Unblocked**: Configured application cookie authentication options, implemented `AccountController` with `LoginViewModel` (Login/Logout/AccessDenied), and enabled role-based authorization (`[Authorize(Roles = ...)]`) for Admin and Mechanic modules.
- **EF Core MS SQL Migration**: Generated fresh initial migration `20260826092543_InitialCreate` targeting MS SQL Server database. Verified all unit/integration tests pass (100% success).

### [2026-08-24] - Final Roadmap Alignment, MS SQL Database Target & MSB1011 Fix
- **Roadmap Alignment**: Adopted `ROADMAP.md` as the official team master plan. Re-aligned all developer vertical slice responsibilities and stage tasks.
- **Database Provider Standard**: Updated primary database configuration to **MS SQL Server** across `appsettings.json` and `DependencyInjection.cs`.
- **MSBuild Error Fix (MSB1011)**: Resolved build failure where MSBuild failed due to multiple solution files (`Garij.sln` and `Garij.slnx`). Updated `.github/workflows/build.yml` and all developer documentation to explicitly specify `Garij.sln` (`dotnet build Garij.sln`, `dotnet test Garij.sln`, `dotnet restore Garij.sln`).
- **Build & Test Verification**: Verified solution compilation and all unit/integration tests pass cleanly.

### [2026-08-24] - Database Schema Definition, EF Core Migrations & Seed Data (Emon)
- Created domain entities `ApplicationUser.cs` and `AiRequestLog.cs`.
- Configured Fluent API database constraints and DbSeeder for roles, admin user, service catalog, and stock parts.

### [2026-08-24] - Architecture Scaffolding & Exception Middleware (Emon)
- Scaffolded solution architecture, implemented global exception middleware, custom exceptions, and test suites.

---

## 🏃 How to Run the Project

### 1. Build
```bash
dotnet build Garij.sln
```

### 2. Run Web Application
```bash
dotnet run --project src/Garij.Web
```

### 3. Run Tests
```bash
dotnet test Garij.sln
```

---

## 🌿 Git Workflow Rules for All Developers & AI Assistants
1. **Branching**: Always checkout a new feature branch from `main`:
   ```bash
   git checkout -b feature/<your-feature-name>
   ```
2. **Never push directly to `main`**.
3. **Update `CONTEXT.md`**: Add your updates under "Recent Progress Log" when completing your task.
4. **Push Branch**: Push your branch to remote:
   ```bash
   git push origin feature/<your-feature-name>
   ```


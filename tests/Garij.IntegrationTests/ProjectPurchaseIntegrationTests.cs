using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Garij.IntegrationTests;

public class ProjectPurchaseIntegrationTests : IClassFixture<AuthorizationTestFactory>
{
    private static readonly Regex AntiForgeryTokenPattern = new(
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
        RegexOptions.Compiled);

    private readonly AuthorizationTestFactory _factory;

    public ProjectPurchaseIntegrationTests(AuthorizationTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateNonRedirectingClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<string> ExtractAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = AntiForgeryTokenPattern.Match(html);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [Fact]
    public async Task AnonymousRequest_ToPurchaseIndex_Returns200OK()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/Purchase");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("UNLOCK FULL GARIJ ACCESS", content);
        Assert.Contains("Lifetime License", content);
    }

    [Fact]
    public async Task SeededAdmin_CanAccessDashboard_BecauseAdminIsSeededWithActiveLicense()
    {
        var client = CreateNonRedirectingClient();

        // Login as admin
        var loginPage = await client.GetAsync("/Account/Login");
        var token = await ExtractAntiForgeryTokenAsync(loginPage);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = token,
        });

        var loginResponse = await client.PostAsync("/Account/Login", form);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // Access dashboard
        var dashboardResponse = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
    }

    [Fact]
    public async Task NewRegisteredUser_WithoutLicense_IsRedirectedToPurchase()
    {
        var client = CreateNonRedirectingClient();

        // Register a new user without buying
        var registerPage = await client.GetAsync("/Account/Register");
        var token = await ExtractAntiForgeryTokenAsync(registerPage);
        var uniqueEmail = $"unlicensed_{Guid.NewGuid():N}@test.com";

        var registerForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Unlicensed User",
            ["Email"] = uniqueEmail,
            ["PhoneNumber"] = "+1999999999",
            ["Password"] = "Test@12345",
            ["ConfirmPassword"] = "Test@12345",
            ["__RequestVerificationToken"] = token,
        });

        var registerResponse = await client.PostAsync("/Account/Register", registerForm);
        Assert.Equal(HttpStatusCode.Redirect, registerResponse.StatusCode);

        // Attempting to access Dashboard should redirect to /Purchase
        var dashboardResponse = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dashboardResponse.StatusCode);
        Assert.Contains("/Purchase", dashboardResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task PurchaseCheckout_GrantsLicense_AndUnlocksDashboard()
    {
        var client = CreateNonRedirectingClient();
        var uniqueEmail = $"purchaser_{Guid.NewGuid():N}@test.com";

        // Register new user
        var registerPage = await client.GetAsync("/Account/Register");
        var token = await ExtractAntiForgeryTokenAsync(registerPage);

        var registerForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Purchasing Garage Owner",
            ["Email"] = uniqueEmail,
            ["PhoneNumber"] = "+1888888888",
            ["Password"] = "Test@12345",
            ["ConfirmPassword"] = "Test@12345",
            ["__RequestVerificationToken"] = token,
        });

        await client.PostAsync("/Account/Register", registerForm);

        // Visit Purchase page to get token
        var purchasePage = await client.GetAsync("/Purchase");
        var purchaseToken = await ExtractAntiForgeryTokenAsync(purchasePage);

        // Submit checkout in test mode
        var checkoutForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BuyerName"] = "Purchasing Garage Owner",
            ["BuyerEmail"] = uniqueEmail,
            ["WorkshopName"] = "Apex Garage",
            ["PaymentMethod"] = "CreditCard",
            ["IsTestPayment"] = "true",
            ["__RequestVerificationToken"] = purchaseToken,
        });

        var checkoutResponse = await client.PostAsync("/Purchase/Checkout", checkoutForm);
        Assert.Equal(HttpStatusCode.Redirect, checkoutResponse.StatusCode);
        Assert.Contains("/Purchase/Success", checkoutResponse.Headers.Location!.ToString());

        // Now, accessing Dashboard should be allowed (200 OK)!
        var dashboardResponse = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
    }

    [Theory]
    [InlineData("/About")]
    [InlineData("/Services")]
    [InlineData("/Testimonials")]
    [InlineData("/Pricing")]
    public async Task AnonymousUser_CanAccessPublicPages_WithoutLicense(string url)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/About")]
    [InlineData("/Services")]
    [InlineData("/Testimonials")]
    [InlineData("/Pricing")]
    public async Task UnlicensedLoggedInUser_CanAccessPublicPages_WhileDashboardIsLocked(string url)
    {
        var client = CreateNonRedirectingClient();
        var uniqueEmail = $"unpaid_public_{Guid.NewGuid():N}@test.com";

        // Register new unlicensed user
        var registerPage = await client.GetAsync("/Account/Register");
        var token = await ExtractAntiForgeryTokenAsync(registerPage);

        var registerForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Unpaid Public Visitor",
            ["Email"] = uniqueEmail,
            ["PhoneNumber"] = "+1777777777",
            ["Password"] = "Test@12345",
            ["ConfirmPassword"] = "Test@12345",
            ["__RequestVerificationToken"] = token,
        });

        await client.PostAsync("/Account/Register", registerForm);

        // 1. Verify public page is accessible with 200 OK
        var pageResponse = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        // 2. Verify dashboard is locked (redirects to /Purchase)
        var dashboardResponse = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, dashboardResponse.StatusCode);
        Assert.Contains("/Purchase", dashboardResponse.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Admin_CanCreateStaffAccount_WithEmailPasswordAndRole_AndStaffCanLogin()
    {
        var adminClient = CreateNonRedirectingClient();

        // 1. Log in as admin (seeded project owner)
        var loginPage = await adminClient.GetAsync("/Account/Login");
        var loginToken = await ExtractAntiForgeryTokenAsync(loginPage);

        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = loginToken,
        });

        var loginResponse = await adminClient.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 2. Access Admin Create User page
        var createPage = await adminClient.GetAsync("/Admin/CreateUser");
        Assert.Equal(HttpStatusCode.OK, createPage.StatusCode);
        var createToken = await ExtractAntiForgeryTokenAsync(createPage);

        // 3. Create a new Mechanic account
        var mechanicEmail = $"mechanic_{Guid.NewGuid():N}@test.com";
        var createForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Master Mechanic Test",
            ["Email"] = mechanicEmail,
            ["PhoneNumber"] = "+880 1711-999888",
            ["Role"] = "Mechanic",
            ["Password"] = "Mechanic@12345",
            ["ConfirmPassword"] = "Mechanic@12345",
            ["__RequestVerificationToken"] = createToken,
        });

        var createResponse = await adminClient.PostAsync("/Admin/CreateUser", createForm);
        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        Assert.Contains("/Admin/ManageUsers", createResponse.Headers.Location!.ToString());

        // 4. Verify the new mechanic can log in with email and password
        var mechanicClient = CreateNonRedirectingClient();
        var mechLoginPage = await mechanicClient.GetAsync("/Account/Login");
        var mechLoginToken = await ExtractAntiForgeryTokenAsync(mechLoginPage);

        var mechLoginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = mechanicEmail,
            ["Password"] = "Mechanic@12345",
            ["__RequestVerificationToken"] = mechLoginToken,
        });

        var mechLoginResponse = await mechanicClient.PostAsync("/Account/Login", mechLoginForm);
        Assert.Equal(HttpStatusCode.Redirect, mechLoginResponse.StatusCode);

        // 5. Verify the mechanic can access the Job Board
        var jobBoardResponse = await mechanicClient.GetAsync("/Mechanic/JobBoard");
        Assert.Equal(HttpStatusCode.OK, jobBoardResponse.StatusCode);
    }

    [Fact]
    public async Task PurchaseCheckout_WithMechanicRole_AssignsMechanicRoleAndGrantsAccess()
    {
        var client = CreateNonRedirectingClient();
        var uniqueEmail = $"buyer_mechanic_{Guid.NewGuid():N}@test.com";

        // 1. Visit Purchase page
        var purchasePage = await client.GetAsync("/Purchase");
        var purchaseToken = await ExtractAntiForgeryTokenAsync(purchasePage);

        // 2. Checkout with Mechanic role and password
        var checkoutForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["BuyerName"] = "Mechanic Owner",
            ["BuyerEmail"] = uniqueEmail,
            ["WorkshopName"] = "Mechanic Pitstop",
            ["PaymentMethod"] = "CreditCard",
            ["Password"] = "Mechanic@12345",
            ["AccountRole"] = "Mechanic",
            ["IsTestPayment"] = "true",
            ["__RequestVerificationToken"] = purchaseToken,
        });

        var checkoutResponse = await client.PostAsync("/Purchase/Checkout", checkoutForm);
        Assert.Equal(HttpStatusCode.Redirect, checkoutResponse.StatusCode);
        Assert.Contains("/Purchase/Success", checkoutResponse.Headers.Location!.ToString());

        // 3. Log in with the created mechanic account
        var loginPage = await client.GetAsync("/Account/Login");
        var loginToken = await ExtractAntiForgeryTokenAsync(loginPage);

        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = uniqueEmail,
            ["Password"] = "Mechanic@12345",
            ["__RequestVerificationToken"] = loginToken,
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 4. Mechanic can access Mechanic Job Board
        var jobBoardResponse = await client.GetAsync("/Mechanic/JobBoard");
        Assert.Equal(HttpStatusCode.OK, jobBoardResponse.StatusCode);
    }

    [Fact]
    public async Task LicensedUser_DoesNotSeePricingInNavbar_OnLandingPage()
    {
        var client = CreateNonRedirectingClient();

        // 1. Unlicensed anonymous user visits landing page: Pricing is visible in navbar
        var anonHome = await client.GetAsync("/");
        var anonHtml = await anonHome.Content.ReadAsStringAsync();
        Assert.Contains("PRICING", anonHtml);
        Assert.Contains("BUY PROJECT", anonHtml);

        // 2. Log in as seeded Admin (who has an active license)
        var loginPage = await client.GetAsync("/Account/Login");
        var loginToken = await ExtractAntiForgeryTokenAsync(loginPage);

        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = loginToken,
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 3. Now visit landing page as licensed user
        var authHome = await client.GetAsync("/");
        var authHtml = await authHome.Content.ReadAsStringAsync();

        // Pricing and Buy Project button must NOT be present in navbar
        Assert.DoesNotContain(">PRICING<", authHtml);
        Assert.DoesNotContain("BUY PROJECT", authHtml);
        Assert.Contains("LICENSED", authHtml);
    }

    [Fact]
    public async Task Jobs_CanAddEmployee_WithEmailPasswordAndRole_AndEmployeeCanLogin()
    {
        var managerClient = CreateNonRedirectingClient();

        // 1. Log in as admin / workshop manager
        var loginPage = await managerClient.GetAsync("/Account/Login");
        var loginToken = await ExtractAntiForgeryTokenAsync(loginPage);

        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = loginToken,
        });

        var loginResponse = await managerClient.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 2. Open Add Employee from Jobs
        var addEmpPage = await managerClient.GetAsync("/ServiceJob/AddEmployee");
        Assert.Equal(HttpStatusCode.OK, addEmpPage.StatusCode);
        var addEmpToken = await ExtractAntiForgeryTokenAsync(addEmpPage);

        // 3. Submit new employee form with role FrontDesk
        var newEmployeeEmail = $"jobs_emp_{Guid.NewGuid():N}@test.com";
        var addEmpForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Job Section Receptionist",
            ["Email"] = newEmployeeEmail,
            ["PhoneNumber"] = "+880 1711-443322",
            ["Role"] = "FrontDesk",
            ["Password"] = "Reception@12345",
            ["ConfirmPassword"] = "Reception@12345",
            ["__RequestVerificationToken"] = addEmpToken,
        });

        var addEmpResponse = await managerClient.PostAsync("/ServiceJob/AddEmployee", addEmpForm);
        Assert.Equal(HttpStatusCode.Redirect, addEmpResponse.StatusCode);
        Assert.Contains("/ServiceJob", addEmpResponse.Headers.Location!.ToString());

        // 4. Log in as the new employee with email and password
        var empClient = CreateNonRedirectingClient();
        var empLoginPage = await empClient.GetAsync("/Account/Login");
        var empLoginToken = await ExtractAntiForgeryTokenAsync(empLoginPage);

        var empLoginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = newEmployeeEmail,
            ["Password"] = "Reception@12345",
            ["__RequestVerificationToken"] = empLoginToken,
        });

        var empLoginResponse = await empClient.PostAsync("/Account/Login", empLoginForm);
        Assert.Equal(HttpStatusCode.Redirect, empLoginResponse.StatusCode);

        // 5. Front Desk employee can access Dashboard and Service Jobs directly without buying a license
        var dashboardResponse = await empClient.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);

        var serviceJobsResponse = await empClient.GetAsync("/ServiceJob");
        Assert.Equal(HttpStatusCode.OK, serviceJobsResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_CanRenameGarage_AndItShowsAtTheTop()
    {
        var client = CreateNonRedirectingClient();

        // 1. Log in as admin (project owner)
        var loginPage = await client.GetAsync("/Account/Login");
        var loginToken = await ExtractAntiForgeryTokenAsync(loginPage);

        var loginForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@garij.com",
            ["Password"] = "Admin@12345",
            ["__RequestVerificationToken"] = loginToken,
        });

        var loginResponse = await client.PostAsync("/Account/Login", loginForm);
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        // 2. Open Garage Settings
        var settingsPage = await client.GetAsync("/Admin/GarageSettings");
        Assert.Equal(HttpStatusCode.OK, settingsPage.StatusCode);
        var settingsToken = await ExtractAntiForgeryTokenAsync(settingsPage);

        // 3. Rename Garage
        var customGarageName = "Apex Turbo Pitstop";
        var updateForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["workshopName"] = customGarageName,
            ["__RequestVerificationToken"] = settingsToken,
        });

        var updateResponse = await client.PostAsync("/Admin/UpdateGarageName", updateForm);
        Assert.Equal(HttpStatusCode.Redirect, updateResponse.StatusCode);

        // 4. Visit Dashboard and verify the custom garage name is displayed at the top!
        var dashboardPage = await client.GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.OK, dashboardPage.StatusCode);
        var dashboardHtml = await dashboardPage.Content.ReadAsStringAsync();
        Assert.Contains(customGarageName, dashboardHtml);
        Assert.Contains("POWERED BY GARIJ", dashboardHtml);

        // 5. Visit Landing page and verify the custom garage name is displayed at the top!
        var landingPage = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, landingPage.StatusCode);
        var landingHtml = await landingPage.Content.ReadAsStringAsync();
        Assert.Contains(customGarageName, landingHtml);
    }

    [Fact]
    public async Task Pages_RenderThemeToggleAndInitializer_ForLightAndDarkMode()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act 1: Landing Page
        var landingResponse = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, landingResponse.StatusCode);
        var landingHtml = await landingResponse.Content.ReadAsStringAsync();

        // Assert 1: Has theme initializer in head and toggle button
        Assert.Contains("garij_theme", landingHtml);
        Assert.Contains("data-theme-toggle", landingHtml);
        Assert.Contains("theme-toggle.js", landingHtml);

        // Act 2: Login Page
        var loginResponse = await client.GetAsync("/Account/Login");
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginHtml = await loginResponse.Content.ReadAsStringAsync();
        Assert.Contains("auth-page-body", loginHtml);
    }
}



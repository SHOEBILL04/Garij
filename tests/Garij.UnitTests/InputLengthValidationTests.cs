using System.ComponentModel.DataAnnotations;
using Garij.Application.DTOs;
using Garij.Domain.Enums;
using Garij.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Garij.UnitTests;

/// <summary>
/// Covers defect D-05 (Report 05): four fields accepted input longer than their database
/// column can store. SQLite does not enforce text length, so over-length values were stored
/// silently; SQL Server rejected the same input at the database.
///
/// The fix is application-level validation on the DTOs the forms bind to, so behaviour is
/// identical on every provider. These tests pin each limit to the value actually configured
/// on the column, so the two cannot drift apart again:
///
///   Vehicle.Color                          50    (VehicleConfiguration)
///   ServiceJob.BookingReference            20    (ServiceJobConfiguration)
///   ServiceJob.DiagnosticNotes           2000    (ServiceJobConfiguration)
///   PaymentTransaction.TransactionReference 100  (PaymentTransactionConfiguration)
///
/// Covers TC-VEH-15, TC-JOB-05, TC-JOB-06, TC-JOB-07 and TC-PAY-09.
/// </summary>
public class InputLengthValidationTests
{
    private const int ColorLimit = 50;
    private const int BookingReferenceLimit = 20;
    private const int DiagnosticNotesLimit = 2000;
    private const int TransactionReferenceLimit = 100;

    /// <summary>Runs the same DataAnnotations validation MVC runs on a posted model.</summary>
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static ValidationResult? ErrorFor(object model, string propertyName) =>
        Validate(model).FirstOrDefault(r => r.MemberNames.Contains(propertyName));

    private static VehicleDto ValidVehicle(string color) => new()
    {
        CustomerId = 1,
        LicensePlateNumber = "DHA-1234",
        Make = "Toyota",
        Model = "Allion",
        Year = 2020,
        Vin = "VIN-TEST-0001",
        Color = color
    };

    private static ServiceJobDto ValidJob(string bookingReference = "GRJ-2026-0001", string? notes = null) => new()
    {
        VehicleId = 1,
        JobType = JobType.RoutineService,
        Status = JobStatus.Requested,
        BookingReference = bookingReference,
        DiagnosticNotes = notes
    };

    private static PaymentTransactionDto ValidPayment(string reference) => new()
    {
        InvoiceId = 1,
        Amount = 10.00m,
        PaymentMethod = PaymentMethod.Cash,
        TransactionReference = reference
    };

    // ---------------------------------------------------------------------------------
    // The declared limits must match the EF Core column configuration, or validation can
    // silently drift away from what the database will accept.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(typeof(Garij.Domain.Entities.Vehicle), nameof(Garij.Domain.Entities.Vehicle.Color), ColorLimit)]
    [InlineData(typeof(Garij.Domain.Entities.ServiceJob), nameof(Garij.Domain.Entities.ServiceJob.BookingReference), BookingReferenceLimit)]
    [InlineData(typeof(Garij.Domain.Entities.ServiceJob), nameof(Garij.Domain.Entities.ServiceJob.DiagnosticNotes), DiagnosticNotesLimit)]
    [InlineData(typeof(Garij.Domain.Entities.PaymentTransaction), nameof(Garij.Domain.Entities.PaymentTransaction.TransactionReference), TransactionReferenceLimit)]
    public void ConfiguredColumnLength_MatchesTheLimitValidationEnforces(Type entityType, string propertyName, int expectedLimit)
    {
        var options = new DbContextOptionsBuilder<GarijDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new GarijDbContext(options);
        var maxLength = context.Model
            .FindEntityType(entityType)!
            .FindProperty(propertyName)!
            .GetMaxLength();

        Assert.Equal(expectedLimit, maxLength);
    }

    // ---------------------------------------------------------------------------------
    // TC-VEH-15: vehicle colour, 50 characters.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void VehicleColor_AtTheLimit_IsAccepted()
    {
        var model = ValidVehicle(new string('a', ColorLimit));

        Assert.Null(ErrorFor(model, nameof(VehicleDto.Color)));
    }

    [Fact]
    public void VehicleColor_OneCharacterOverTheLimit_IsRejectedWithAClearMessage()
    {
        var model = ValidVehicle(new string('a', ColorLimit + 1));

        var error = ErrorFor(model, nameof(VehicleDto.Color));

        Assert.NotNull(error);
        Assert.Equal("Colour cannot exceed 50 characters.", error!.ErrorMessage);
    }

    [Fact]
    public void VehicleColor_At100Characters_IsRejected()
    {
        // The exact value from TC-VEH-15.
        var model = ValidVehicle(new string('a', 100));

        Assert.NotNull(ErrorFor(model, nameof(VehicleDto.Color)));
    }

    // ---------------------------------------------------------------------------------
    // TC-JOB-07: booking reference, 20 characters.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void BookingReference_AtTheLimit_IsAccepted()
    {
        var model = ValidJob(new string('R', BookingReferenceLimit));

        Assert.Null(ErrorFor(model, nameof(ServiceJobDto.BookingReference)));
    }

    [Fact]
    public void BookingReference_OneCharacterOverTheLimit_IsRejectedWithAClearMessage()
    {
        var model = ValidJob(new string('R', BookingReferenceLimit + 1));

        var error = ErrorFor(model, nameof(ServiceJobDto.BookingReference));

        Assert.NotNull(error);
        Assert.Equal("Booking reference cannot exceed 20 characters.", error!.ErrorMessage);
    }

    [Fact]
    public void BookingReference_At50Characters_IsRejected()
    {
        // The exact value from TC-JOB-07.
        var model = ValidJob(new string('R', 50));

        Assert.NotNull(ErrorFor(model, nameof(ServiceJobDto.BookingReference)));
    }

    [Fact]
    public void BookingReference_LeftEmpty_IsNotRejectedByTheLengthLimit()
    {
        // The length limit must not itself make the field required. (MVC separately treats the
        // non-nullable property as required on the Create form - pre-existing behaviour that
        // this change neither introduces nor alters.)
        var model = ValidJob(string.Empty);

        Assert.Null(ErrorFor(model, nameof(ServiceJobDto.BookingReference)));
    }

    // ---------------------------------------------------------------------------------
    // TC-JOB-05 / TC-JOB-06: diagnostic notes, 2000 characters.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void DiagnosticNotes_At2000Characters_IsAccepted()
    {
        // TC-JOB-05, which passed before this change and must keep passing.
        var model = ValidJob(notes: new string('n', DiagnosticNotesLimit));

        Assert.Null(ErrorFor(model, nameof(ServiceJobDto.DiagnosticNotes)));
    }

    [Fact]
    public void DiagnosticNotes_OneCharacterOverTheLimit_IsRejectedWithAClearMessage()
    {
        var model = ValidJob(notes: new string('n', DiagnosticNotesLimit + 1));

        var error = ErrorFor(model, nameof(ServiceJobDto.DiagnosticNotes));

        Assert.NotNull(error);
        Assert.Equal("Diagnostic notes cannot exceed 2000 characters.", error!.ErrorMessage);
    }

    [Fact]
    public void DiagnosticNotes_At5000Characters_IsRejected()
    {
        // The exact value from TC-JOB-06.
        var model = ValidJob(notes: new string('n', 5000));

        Assert.NotNull(ErrorFor(model, nameof(ServiceJobDto.DiagnosticNotes)));
    }

    [Fact]
    public void DiagnosticNotes_LeftNull_IsStillAccepted()
    {
        var model = ValidJob(notes: null);

        Assert.Null(ErrorFor(model, nameof(ServiceJobDto.DiagnosticNotes)));
    }

    // ---------------------------------------------------------------------------------
    // TC-PAY-09: transaction reference, 100 characters.
    // ---------------------------------------------------------------------------------

    [Fact]
    public void TransactionReference_AtTheLimit_IsAccepted()
    {
        var model = ValidPayment(new string('T', TransactionReferenceLimit));

        Assert.Null(ErrorFor(model, nameof(PaymentTransactionDto.TransactionReference)));
    }

    [Fact]
    public void TransactionReference_OneCharacterOverTheLimit_IsRejectedWithAClearMessage()
    {
        var model = ValidPayment(new string('T', TransactionReferenceLimit + 1));

        var error = ErrorFor(model, nameof(PaymentTransactionDto.TransactionReference));

        Assert.NotNull(error);
        Assert.Equal("Transaction reference cannot exceed 100 characters.", error!.ErrorMessage);
    }

    [Fact]
    public void TransactionReference_At200Characters_IsRejected()
    {
        // The exact value from TC-PAY-09.
        var model = ValidPayment(new string('T', 200));

        Assert.NotNull(ErrorFor(model, nameof(PaymentTransactionDto.TransactionReference)));
    }

    [Fact]
    public void TransactionReference_LeftEmpty_IsStillAccepted()
    {
        var model = ValidPayment(string.Empty);

        Assert.Null(ErrorFor(model, nameof(PaymentTransactionDto.TransactionReference)));
    }
}

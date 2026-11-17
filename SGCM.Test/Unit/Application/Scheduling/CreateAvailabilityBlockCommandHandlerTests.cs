using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CreateAvailabilityBlockCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int BlockId = 21;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorAvailabilityRepository _blocks = Substitute.For<IDoctorAvailabilityRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    // Lunes 4 de enero de 2027 a las 8:00 en Santo Domingo.
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.Zero) };
    private readonly SchedulingOptions _options = CreateAvailabilitySeriesCommandMother.Options();
    private DoctorAvailability? _added;

    public CreateAvailabilityBlockCommandHandlerTests()
    {
        _currentUser.UserId.Returns(UserId);
        var doctor = DoctorMother.Create([1]).Value;
        SetId(doctor, DoctorId);
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns(doctor);
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability>());
        _blocks.When(repository => repository.Add(Arg.Any<DoctorAvailability>()))
            .Do(call => _added = call.Arg<DoctorAvailability>());
        // Simula la base: el ID se genera al guardar.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => SetId(_added!, BlockId));
    }

    [Fact]
    public async Task Crea_un_bloque_suelto_del_medico_de_la_sesion_y_devuelve_su_id()
    {
        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid());

        result.Value.ShouldBe(BlockId);
        _added.ShouldNotBeNull();
        _added.DoctorId.ShouldBe(DoctorId);
        _added.SeriesId.ShouldBeNull();
        _added.StartsAt.ShouldBe(CreateAvailabilityBlockCommandMother.StartsAtUtc);
        _added.EndsAt.ShouldBe(CreateAvailabilityBlockCommandMother.EndsAtUtc);
        _added.IsAvailable.ShouldBeTrue();
        _added.IsModified.ShouldBeFalse();
        Received.InOrder(() =>
        {
            _doctors.GetByUserIdForUpdateAsync(UserId, CancellationToken.None);
            _blocks.ListByDoctorAsync(DoctorId, CreateAvailabilityBlockCommandMother.StartsAtUtc,
                CreateAvailabilityBlockCommandMother.EndsAtUtc, CancellationToken.None);
            _blocks.Add(Arg.Any<DoctorAvailability>());
            _unitOfWork.SaveChangesAsync(CancellationToken.None);
        });
    }

    [Fact]
    public async Task Una_fecha_pasada_se_rechaza_sin_consultar_la_base()
    {
        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid() with { Date = new DateOnly(2027, 1, 3) });

        result.Error.ShouldBe(DoctorAvailabilityErrors.StartsInPast);
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _blocks.ReceivedCalls().ShouldBeEmpty();
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_bloque_de_hoy_que_ya_empezo_se_rechaza()
    {
        // Martes 5 de enero a las 9:00 en Santo Domingo: el bloque de 8:00 ya empezó.
        _clock.UtcNow = new DateTimeOffset(2027, 1, 5, 13, 0, 0, TimeSpan.Zero);

        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid());

        result.Error.ShouldBe(DoctorAvailabilityErrors.StartsInPast);
    }

    [Fact]
    public async Task Un_bloque_de_mas_tarde_hoy_se_acepta()
    {
        // Martes 5 de enero a las 7:00 en Santo Domingo.
        _clock.UtcNow = new DateTimeOffset(2027, 1, 5, 11, 0, 0, TimeSpan.Zero);

        (await Handle(CreateAvailabilityBlockCommandMother.Valid())).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_perfil_de_medico_responde_NotFound_sin_guardar()
    {
        _doctors.GetByUserIdForUpdateAsync(UserId, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid());

        result.Error.ShouldBe(DoctorErrors.NotFound);
        _blocks.DidNotReceive().Add(Arg.Any<DoctorAvailability>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Se_rechaza_si_se_traslapa_con_otro_bloque_del_medico()
    {
        var existing = DoctorAvailability.Create(
            DoctorId, null, CreateAvailabilityBlockCommandMother.StartsAtUtc.AddHours(3),
            CreateAvailabilityBlockCommandMother.StartsAtUtc.AddHours(9), 6).Value;
        _blocks.ListByDoctorAsync(DoctorId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DoctorAvailability> { existing });

        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid());

        result.Error.ShouldBe(DoctorAvailabilityErrors.Overlap);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        _blocks.DidNotReceive().Add(Arg.Any<DoctorAvailability>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(8, 30, 14, 0, "Availability.NotOnHour")]
    [InlineData(8, 0, 10, 0, "Availability.DurationTooShort")]
    [InlineData(14, 0, 8, 0, "Availability.InvalidTimeRange")]
    public async Task Un_error_del_dominio_se_devuelve_sin_guardar(
        int startHour, int startMinute, int endHour, int endMinute, string code)
    {
        var result = await Handle(CreateAvailabilityBlockCommandMother.Valid() with
        {
            StartTime = new TimeOnly(startHour, startMinute),
            EndTime = new TimeOnly(endHour, endMinute)
        });

        result.Error.Code.ShouldBe(code);
        _blocks.DidNotReceive().Add(Arg.Any<DoctorAvailability>());
    }

    [Fact]
    public async Task La_duracion_minima_sale_de_la_configuracion()
    {
        _options.MinimumBlockDurationHours = 8;

        (await Handle(CreateAvailabilityBlockCommandMother.Valid())).Error.ShouldBe(DoctorAvailabilityErrors.DurationTooShort);
    }

    private Task<Result<int>> Handle(CreateAvailabilityBlockCommand command) =>
        new CreateAvailabilityBlockCommandHandler(
                _currentUser, _doctors, _blocks, _unitOfWork, _clock,
                BusinessCalendar.ForTimeZone(_options.TimeZone), Options.Create(_options))
            .HandleAsync(command, CancellationToken.None);

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);
}

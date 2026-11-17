using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="CreateAvailabilityBlockCommand"/>: el martes 5 de enero de 2027 de 8:00 a 14:00
/// en Santo Domingo, es decir, de 12:00 a 18:00 UTC.
/// </summary>
public static class CreateAvailabilityBlockCommandMother
{
    public static readonly DateOnly Date = new(2027, 1, 5);
    public static readonly DateTimeOffset StartsAtUtc = new(2027, 1, 5, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset EndsAtUtc = new(2027, 1, 5, 18, 0, 0, TimeSpan.Zero);

    public static CreateAvailabilityBlockCommand Valid() => new(Date, new TimeOnly(8, 0), new TimeOnly(14, 0));
}

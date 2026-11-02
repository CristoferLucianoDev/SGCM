using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
[Collection(DatabaseCollection.Name)]
public sealed class SpecialtyListReaderTests(PostgresFixture database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task List_filtra_en_PostgreSQL_y_cuenta_antes_de_paginar_con_orden_estable(bool? state)
    {
        var capture = new ExecutedCommands();
        await using var context = CreateContext(capture);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var bravo = Specialty.Create("Bravo", "Segunda activa.").Value;
        var alfa = Specialty.Create("Alfa", "Primera activa.").Value;
        var delta = Specialty.Create("Delta", "Segunda inactiva.").Value;
        var charlie = Specialty.Create("Charlie", "Primera inactiva.").Value;
        delta.Deactivate().IsSuccess.ShouldBeTrue();
        charlie.Deactivate().IsSuccess.ShouldBeTrue();
        context.Specialties.AddRange(bravo, alfa, delta, charlie);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        capture.Commands.Clear();
        Specialty[] ordered = [alfa, bravo, charlie, delta];
        var expected = ordered.Where(value => !state.HasValue || value.IsActive == state.Value).ToArray();

        var page = await new SpecialtyListReader(context).ListAsync(null, state, true, 2, 1, CancellationToken.None);

        page.TotalCount.ShouldBe(expected.Length);
        page.PageNumber.ShouldBe(2);
        page.PageSize.ShouldBe(1);
        var item = page.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(expected[1].Id);
        item.Name.ShouldBe(expected[1].Name);
        item.Description.ShouldBe(expected[1].Description);
        item.IsActive.ShouldBe(expected[1].IsActive);
        context.ChangeTracker.Entries().ShouldBeEmpty();
        capture.Commands.Count.ShouldBe(2);
        capture.Commands[0].Sql.ShouldContain("count(*)");
        capture.Commands[0].Sql.ShouldNotContain("LIMIT");
        capture.Commands[0].Sql.ShouldNotContain("OFFSET");
        capture.Commands[1].Sql.ShouldContain("ORDER BY s.name, s.id");
        capture.Commands[1].Sql.ShouldContain("LIMIT @");
        capture.Commands[1].Sql.ShouldContain("OFFSET @");
        var ids = ordered.Select(value => value.Id).ToArray();
        await transaction.RollbackAsync();
        (await context.Specialties.AnyAsync(value => ids.Contains(value.Id))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task List_ejecuta_la_normalizacion_SQL_proyecta_DTO_y_respeta_el_token_sin_tracking(bool includeStatus)
    {
        var capture = new ExecutedCommands();
        await using var context = CreateContext(capture);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var specialty = Specialty.Create("Ginecología", "Salud ginecológica.").Value;
        var inactive = Specialty.Create("Ginecología Pediátrica", "Registro inactivo.").Value;
        var other = Specialty.Create("Medicina Interna", "Otra.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        context.Specialties.AddRange(specialty, inactive, other);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        capture.Commands.Clear();
        context.ChangeTracker.QueryTrackingBehavior.ShouldBe(QueryTrackingBehavior.TrackAll);
        using var cancellation = new CancellationTokenSource();

        var page = await new SpecialtyListReader(context).ListAsync("  GINÉCO  ", true, includeStatus, 1, 20, cancellation.Token);

        page.TotalCount.ShouldBe(1);
        var item = page.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(specialty.Id);
        item.Name.ShouldBe("Ginecología");
        item.Description.ShouldBe(specialty.Description);
        item.IsActive.ShouldBe(includeStatus ? true : null);
        context.ChangeTracker.Entries().ShouldBeEmpty();
        capture.Commands.Count.ShouldBe(2);
        foreach (var command in capture.Commands)
        {
            command.Token.ShouldBe(cancellation.Token);
            command.Sql.ShouldContain("FROM people.specialty");
            command.Sql.ShouldContain("strpos(people.sp_text_normalize(s.name), people.sp_text_normalize(@");
            Regex.Matches(command.Sql, @"people\.sp_text_normalize\(").Count.ShouldBe(2);
            command.Parameters.ShouldContain("  GINÉCO  ");
        }
        var sql = capture.Commands[1].Sql;
        var projection = sql[..sql.IndexOf("FROM", StringComparison.Ordinal)];
        projection.ShouldContain("s.id");
        projection.ShouldContain("s.name");
        projection.ShouldContain("s.description");
        projection.Contains("s.is_active", StringComparison.Ordinal).ShouldBe(includeStatus);
        int[] ids = [specialty.Id, inactive.Id, other.Id];
        await transaction.RollbackAsync();
        (await context.Specialties.AnyAsync(value => ids.Contains(value.Id))).ShouldBeFalse();
    }

    [Fact]
    public async Task List_con_token_cancelado_no_ejecuta_la_lectura()
    {
        var capture = new ExecutedCommands();
        await using var context = CreateContext(capture);
        await using var transaction = await context.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new SpecialtyListReader(context).ListAsync(null, true, false, 1, 20, cancellation.Token));

        capture.Commands.ShouldBeEmpty();
    }

    private AppDbContext CreateContext(ExecutedCommands capture) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(database.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(capture)
        .Options);

    private sealed record ExecutedCommand(string Sql, object?[] Parameters, CancellationToken Token);

    // Observa comandos completados; no suprime conexiones ni sustituye resultados de PostgreSQL.
    private sealed class ExecutedCommands : DbCommandInterceptor
    {
        public List<ExecutedCommand> Commands { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new ExecutedCommand(command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray(), cancellationToken));
            return ValueTask.FromResult(result);
        }
    }
}

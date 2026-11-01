using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class SpecialtyListReaderTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(null, true)]
    [InlineData(true, false)]
    public async Task List_genera_SQL_con_filtros_comunes_conteo_proyeccion_y_paginacion(bool? state, bool includeStatus)
    {
        var itemIsActive = state ?? false;
        var commands = new StubCommands(7, true, itemIsActive);
        await using var context = CreateContext(commands);
        using var cancellation = new CancellationTokenSource();

        var result = await new SpecialtyListReader(context).ListAsync("GINÉCO%_", state, includeStatus, 3, 2, cancellation.Token);

        result.TotalCount.ShouldBe(7);
        result.PageNumber.ShouldBe(3);
        result.PageSize.ShouldBe(2);
        var item = result.Items.ShouldHaveSingleItem();
        item.Id.ShouldBe(42);
        item.Name.ShouldBe("Ginecología");
        item.Description.ShouldBe("Descripción.");
        item.IsActive.ShouldBe(includeStatus ? itemIsActive : null);
        context.ChangeTracker.Entries().ShouldBeEmpty();
        commands.Sql.Count.ShouldBe(2);
        commands.Sql[0].ShouldContain("count(*)");
        commands.Sql[0].ShouldNotContain("ORDER BY");
        commands.Sql[0].ShouldNotContain("LIMIT");
        commands.Sql[0].ShouldNotContain("OFFSET");
        for (var index = 0; index < commands.Sql.Count; index++)
        {
            var sql = commands.Sql[index];
            sql.ShouldContain("FROM people.specialty");
            var searchParameter = Regex.Match(sql,
                @"strpos\(people\.sp_text_normalize\(s\.name\), people\.sp_text_normalize\((@\w+)\)\) > 0");
            searchParameter.Success.ShouldBeTrue(sql);
            commands.Parameters[index][searchParameter.Groups[1].Value.TrimStart('@')].ShouldBe("GINÉCO%_");
            sql.ShouldNotContain(" LIKE ");
            sql.ShouldNotContain(" ILIKE ");
            var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..]
                .Split("ORDER BY", StringSplitOptions.None)[0].TrimEnd();
            if (state.HasValue)
            {
                var stateParameter = Regex.Match(where, @"s\.is_active = (@\w+)");
                stateParameter.Success.ShouldBeTrue(sql);
                commands.Parameters[index][stateParameter.Groups[1].Value.TrimStart('@')].ShouldBe(state.Value);
            }
            else
            {
                where.ShouldNotContain("is_active");
            }
        }

        commands.Sql[0][commands.Sql[0].IndexOf("WHERE", StringComparison.Ordinal)..]
            .ShouldBe(commands.Sql[1][commands.Sql[1].IndexOf("WHERE", StringComparison.Ordinal)..]
                .Split("ORDER BY", StringSplitOptions.None)[0].TrimEnd());
        var pageSql = commands.Sql[1];
        pageSql.ShouldContain("ORDER BY s.name, s.id");
        var limitParameter = Regex.Match(pageSql, @"LIMIT (@\w+)");
        var offsetParameter = Regex.Match(pageSql, @"OFFSET (@\w+)");
        limitParameter.Success.ShouldBeTrue(pageSql);
        offsetParameter.Success.ShouldBeTrue(pageSql);
        commands.Parameters[1][limitParameter.Groups[1].Value.TrimStart('@')].ShouldBe(2);
        commands.Parameters[1][offsetParameter.Groups[1].Value.TrimStart('@')].ShouldBe(4);
        var projection = pageSql[..pageSql.IndexOf("FROM", StringComparison.Ordinal)];
        projection.ShouldContain("s.id");
        projection.ShouldContain("s.name");
        projection.ShouldContain("s.description");
        projection.Contains("s.is_active", StringComparison.Ordinal).ShouldBe(includeStatus);
        commands.Tokens.ShouldAllBe(token => token == cancellation.Token);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task List_propaga_cancelacion_del_conteo_o_de_la_pagina_sin_ejecutar_mas_comandos(int cancelAt)
    {
        using var cancellation = new CancellationTokenSource();
        var commands = new StubCommands(7, true, beforeRead: count =>
        {
            if (count == cancelAt)
            {
                cancellation.Cancel();
            }
        });
        await using var context = CreateContext(commands);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            new SpecialtyListReader(context).ListAsync(null, true, false, 1, 20, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        commands.Sql.Count.ShouldBe(cancelAt);
        commands.Tokens.ShouldAllBe(token => token == cancellation.Token);
    }

    [Fact]
    public async Task List_sin_busqueda_no_invoca_normalizacion_y_devuelve_un_vacio_exitoso()
    {
        var commands = new StubCommands(0, false);
        await using var context = CreateContext(commands);

        var result = await new SpecialtyListReader(context).ListAsync(null, true, false, 1, 20, CancellationToken.None);

        result.Items.ShouldBeEmpty();
        result.TotalCount.ShouldBe(0);
        commands.Sql.ShouldAllBe(sql => !sql.Contains("sp_text_normalize", StringComparison.Ordinal));
    }

    [Fact]
    public void AddInfrastructure_registra_el_lector_con_alcance_por_solicitud()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure();
        services.AddSingleton(Substitute.For<Serilog.ILogger>());
        services.AddSingleton(Options.Create(new DatabaseOptions
        {
            SgcmDatabase = "Host=localhost;Database=unused;Username=unused;Password=unused"
        }));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        var reader = scope.ServiceProvider.GetRequiredService<ISpecialtyListReader>();

        reader.ShouldBeOfType<SpecialtyListReader>();
        scope.ServiceProvider.GetRequiredService<ISpecialtyListReader>().ShouldBeSameAs(reader);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<ISpecialtyListReader>().ShouldNotBeSameAs(reader);
    }

    private static AppDbContext CreateContext(StubCommands commands) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
        .UseSnakeCaseNamingConvention()
        .AddInterceptors(new StubConnection(), commands)
        .Options);

    // Se compila el LINQ con Npgsql, pero ninguna conexión ni comando llega a PostgreSQL.
    private sealed class StubConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class StubCommands(
        int totalCount, bool hasItems, bool itemIsActive = true, Action<int>? beforeRead = null) : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<Dictionary<string, object?>> Parameters { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            Parameters.Add(command.Parameters.Cast<DbParameter>()
                .ToDictionary(parameter => parameter.ParameterName.TrimStart('@'), parameter => parameter.Value));
            Tokens.Add(cancellationToken);
            beforeRead?.Invoke(Sql.Count);
            cancellationToken.ThrowIfCancellationRequested();
            using var table = new DataTable();
            if (command.CommandText.Contains("count(*)", StringComparison.Ordinal))
            {
                table.Columns.Add("count", typeof(int));
                table.Rows.Add(totalCount);
            }
            else
            {
                table.Columns.Add("id", typeof(int));
                table.Columns.Add("name", typeof(string));
                table.Columns.Add("description", typeof(string));
                table.Columns.Add("is_active", typeof(bool));
                if (hasItems)
                {
                    table.Rows.Add(42, "Ginecología", "Descripción.", itemIsActive);
                }
            }

            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}

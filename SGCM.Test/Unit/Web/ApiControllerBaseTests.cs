using Microsoft.AspNetCore.Http;
using Shouldly;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;

namespace SGCM.Test.Unit.Web;

public sealed class ApiControllerBaseTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.TooManyRequests, StatusCodes.Status429TooManyRequests)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void ToStatusCode_MapsEachErrorType(ErrorType type, int expected)
    {
        ApiControllerBase.ToStatusCode(type).ShouldBe(expected);
    }
}
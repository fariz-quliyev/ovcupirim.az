using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ovcuprim.Api.Infrastructure;

/// <summary>
/// Runs the FluentValidation validator registered for each action argument before the action
/// executes, and turns any failure into an RFC 7807 validation problem with per-field messages.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument));

            if (result.IsValid)
            {
                continue;
            }

            foreach (var group in result.Errors.GroupBy(e => e.PropertyName))
            {
                errors[ToCamelCase(group.Key)] = group.Select(e => e.ErrorMessage).ToArray();
            }
        }

        if (errors.Count > 0)
        {
            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = "Göndərilən məlumatlar düzgün deyil.",
                Instance = context.HttpContext.Request.Path
            })
            {
                ContentTypes = { "application/problem+json" }
            };

            return;
        }

        await next();
    }

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];
}

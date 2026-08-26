using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.SharedKernel.Tests;

/// <summary>Results carry expected failures as values, so these are the rules of that contract.</summary>
public sealed class ResultTests
{
    [Fact]
    public void A_success_carries_its_value()
    {
        Result<int> result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_is_a_programming_error()
    {
        Result<int> result = Result.Failure<int>(Error.NotFound("part.not_found", "No such part."));

        Action read = () => _ = result.Value;

        read.Should().Throw<InvalidOperationException>().WithMessage("*part.not_found*");
    }

    [Fact]
    public void An_error_converts_implicitly_so_guard_clauses_stay_readable()
    {
        Result result = Error.Validation("sku.required", "A SKU is required.");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("sku.required");
    }

    [Fact]
    public void Map_projects_a_success_and_passes_a_failure_through()
    {
        Result.Success(21).Map(value => value * 2).Value.Should().Be(42);

        Result<int> failed = Result.Failure<int>(Error.Conflict("dup", "Duplicate."));
        failed.Map(value => value * 2).Error.Code.Should().Be("dup");
    }

    [Fact]
    public void Bind_chains_fallible_steps_and_stops_at_the_first_failure()
    {
        Result<int> chained = Result.Success(10)
            .Bind(value => Result.Success(value + 1))
            .Bind(_ => Result.Failure<int>(Error.DomainRule("rule", "Not allowed.")))
            .Bind(value => Result.Success(value * 100));

        chained.IsFailure.Should().BeTrue();
        chained.Error.Code.Should().Be("rule");
    }

    [Fact]
    public void Match_collapses_both_branches()
    {
        string message = Result.Failure<int>(Error.NotFound("gone", "Not here."))
            .Match(value => $"got {value}", error => $"failed: {error.Code}");

        message.Should().Be("failed: gone");
    }

    [Fact]
    public void A_validation_error_keeps_every_field_problem()
    {
        var error = new ValidationError(
        [
            new ValidationFailure("Sku", "required", "A SKU is required."),
            new ValidationFailure("Name", "required", "A description is required."),
        ]);

        error.Type.Should().Be(ErrorType.Validation);
        error.Failures.Should().HaveCount(2);
        error.Description.Should().Contain("2 problems");
    }

    [Fact]
    public void A_failure_must_carry_an_error()
    {
        Action failureWithoutError = () => _ = Result.Failure(Error.None);

        failureWithoutError.Should().Throw<InvalidOperationException>()
            .WithMessage("*must carry an error*");
    }
}

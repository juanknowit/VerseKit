using FluentAssertions;
using VerseKit.PluginSdk;
using Xunit;

namespace VerseKit.Tests;

public class ResultTests
{
    [Fact]
    public void Success_carries_the_value_and_no_error()
    {
        var r = Result<int>.Success(42);

        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
        r.ErrorMessage.Should().BeNull();
        r.Exception.Should().BeNull();
    }

    [Fact]
    public void Failure_carries_message_and_exception()
    {
        var ex = new InvalidOperationException("boom");
        var r = Result<string>.Failure("nope", ex);

        r.IsSuccess.Should().BeFalse();
        r.Value.Should().BeNull();
        r.ErrorMessage.Should().Be("nope");
        r.Exception.Should().BeSameAs(ex);
    }

    [Fact]
    public void Map_transforms_a_success()
    {
        var mapped = Result<int>.Success(21).Map(v => v * 2);

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be(42);
    }

    [Fact]
    public void Map_propagates_failure_without_invoking_the_mapper()
    {
        var invoked = false;
        var mapped = Result<int>.Failure("bad").Map(v => { invoked = true; return v * 2; });

        invoked.Should().BeFalse();
        mapped.IsSuccess.Should().BeFalse();
        mapped.ErrorMessage.Should().Be("bad");
    }
}

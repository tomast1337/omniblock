namespace OmniBlock.Tests;

public class PlayerNameValidatorTests
{
    [Fact]
    public void Validate_accepts_sixteen_chars_without_whitespace()
    {
        string name = new('a', PlayerNameValidator.MaxLength);
        PlayerNameValidator.Validate(name);
    }

    [Fact]
    public void Validate_accepts_default_style_name() => PlayerNameValidator.Validate("Player123456789");

    [Fact]
    public void Validate_throws_when_longer_than_max()
    {
        string name = new('a', PlayerNameValidator.MaxLength + 1);
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate(name));
        Assert.Contains("16", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_throws_on_interior_space()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate("Foo Bar"));
        Assert.Equal(InvalidPlayerNameException.InvalidChar().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_whitespace_only()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate("   "));
        Assert.Equal(InvalidPlayerNameException.NameEmpty().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_empty_string()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate(""));
        Assert.Equal(InvalidPlayerNameException.NameEmpty().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_too_short()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate("A"));
        Assert.Equal(InvalidPlayerNameException.TooShort().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_leading_whitespace()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate(" xxx"));
        Assert.Equal(InvalidPlayerNameException.TrimDifferent().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_trailing_whitespace()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate("xxx "));
        Assert.Equal(InvalidPlayerNameException.TrimDifferent().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_tab_character()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate("foo\tbar"));
        Assert.Equal(InvalidPlayerNameException.InvalidChar().Message, ex.Message);
    }

    [Fact]
    public void Validate_throws_on_null()
    {
        var ex = Assert.Throws<InvalidPlayerNameException>(() => PlayerNameValidator.Validate(null));
        Assert.Equal(InvalidPlayerNameException.NameNull().Message, ex.Message);
    }
}

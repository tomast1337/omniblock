namespace OmniBlock;

/// <summary>
///     Thrown when a player display name violates <see cref="PlayerNameValidator" /> rules.
/// </summary>
public sealed class InvalidPlayerNameException : ArgumentException
{
    public InvalidPlayerNameException(string message) : base(message)
    {
    }

    public static InvalidPlayerNameException NameNull() => new("Player name is required.");
    public static InvalidPlayerNameException NameEmpty() => new("Player name cannot be empty.");
    public static InvalidPlayerNameException TooShort() => new("Player name cannot be shorter than 3 letters.");
    public static InvalidPlayerNameException TrimDifferent() => new("Player name cannot have leading or trailing whitespace.");
    public static InvalidPlayerNameException TooLong() => new($"Player name cannot be longer than {PlayerNameValidator.MaxLength} characters.");
    public static InvalidPlayerNameException InvalidChar() => new("Player name may not contain illegal characters. Only [a-zA-Z0-9_] are allowed.");
}

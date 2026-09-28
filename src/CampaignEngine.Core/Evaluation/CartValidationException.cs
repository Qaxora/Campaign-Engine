namespace CampaignEngine.Core.Evaluation;

/// <summary>Thrown when a cart cannot be evaluated because it is malformed.</summary>
public sealed class CartValidationException : Exception
{
    public CartValidationException()
        : this([])
    {
    }

    public CartValidationException(string message)
        : this([message])
    {
    }

    public CartValidationException(string message, Exception innerException)
        : base(message, innerException) => Errors = [message];

    public CartValidationException(IReadOnlyList<string> errors)
        : base("The cart is invalid: " + string.Join(" ", errors)) => Errors = errors;

    public IReadOnlyList<string> Errors { get; }
}

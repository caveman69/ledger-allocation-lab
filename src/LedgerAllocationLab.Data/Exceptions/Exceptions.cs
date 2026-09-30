using System.Text.RegularExpressions;

namespace LedgerAllocationLab.Data.Exceptions;

public static class LogTemplates
{
    public const string IdempotencyKeyConflictExceptionTemplate = "Idempotency violation: {IdempotencyKey} maps to different payment existing/requested values: ParcelId {ExistingParcelId}/{ParcelId}, TaxYear: {ExistingTaxYear}/{TaxYear}, AmountCents: {ExistingAmountCents}/{AmountCents}.";
}

public class IdempotencyKeyConflictException(Exception innerException, string messageTemplate, params object[] args) : StructuredException(innerException, messageTemplate, args)
{
}

public class StructuredException : Exception
{
    public string MessageTemplate { get; }
    public object[] TemplateArgs { get; }

    public StructuredException(Exception innerException, string messageTemplate, params object[] args)
        : base(RenderTemplate(messageTemplate, args), innerException) // Set the human-readable message
    {
        MessageTemplate = messageTemplate;
        TemplateArgs = args;
    }

    // Helper to turn "{OrderId} failed" into standard positional string.Format slots
    private static string RenderTemplate(string template, object[] args)
    {
        int index = 0;
        string positionalTemplate = Regex.Replace(template, @"\{[a-zA-Z0-9_]+?\}", _ => $"{{{index++}}}");
        return string.Format(positionalTemplate, args);
    }
}

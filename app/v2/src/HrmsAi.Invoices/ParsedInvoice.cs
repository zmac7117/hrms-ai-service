namespace HrmsAi.Invoices;

// PUBLIC OUTPUT CONTRACT (schemaVersion 1.0). Unknown single values are null, lists are never null.
public class ParsedInvoice
{
    public string SchemaVersion { get; set; } = "1.0";
    public InvoiceDocument Document { get; set; } = new();
    public InvoiceVendor Vendor { get; set; } = new();
    public InvoiceHeader Invoice { get; set; } = new();
    public string? Currency { get; set; }
    public string? ExpenseCategory { get; set; }
    public InvoiceTotals Totals { get; set; } = new();
    public List<InvoiceLine> LineItems { get; set; } = new();
    public InvoiceChecks Checks { get; set; } = new();
    public DuplicateInfo? PossibleDuplicate { get; set; }
    public List<string> Warnings { get; set; } = new();
    public InvoiceMeta Meta { get; set; } = new();
}

public class InvoiceDocument
{
    public string Type { get; set; } = "other";      // invoice | receipt | bill | other
    public string Source { get; set; } = "";         // pdf-text | ocr
    public int Pages { get; set; }
}

public class InvoiceVendor
{
    public string? Name { get; set; }
    public string? TaxId { get; set; }
    public string? Address { get; set; }
}

public class InvoiceHeader
{
    public string? Number { get; set; }
    public string? Date { get; set; }                // yyyy-MM-dd
    public string? DateText { get; set; }            // as printed
    public string? DueDate { get; set; }
    public string? DueDateText { get; set; }
    public string? PurchaseOrder { get; set; }
}

public class InvoiceTotals
{
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal? Discount { get; set; }
    public decimal? Shipping { get; set; }
    public decimal? Total { get; set; }
}

public class InvoiceLine
{
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal? Amount { get; set; }
}

public class InvoiceChecks
{
    public bool? LineItemsMatchSubtotal { get; set; }
    public bool? TotalsAddUp { get; set; }
}

public class DuplicateInfo
{
    public string RequestId { get; set; } = "";
    public DateTime At { get; set; }
}

public class InvoiceMeta
{
    public string Model { get; set; } = "";
    public DateTime ParsedAtUtc { get; set; }
}

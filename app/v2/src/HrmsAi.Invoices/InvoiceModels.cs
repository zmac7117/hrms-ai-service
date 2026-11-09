using System.ComponentModel;

namespace HrmsAi.Invoices;

public class InvoiceSettings
{
    public const string SectionName = "Invoice";

    public int MaxFileSizeMb { get; set; } = 10;
    public string StoragePath { get; set; } = @"C:\HRMS AI\data\invoices";
    public bool KeepFiles { get; set; } = false;          // false = the uploaded file is deleted once it was read
    public string DateOrder { get; set; } = "DMY";        // how 03/04/2026 is read: DMY (day first) or MDY
    public string? DefaultCurrency { get; set; }           // used when the document shows no currency, for example "INR"
    public bool IncludeLineItems { get; set; } = true;     // false = totals only (faster)
    public int MaxLineItems { get; set; } = 30;
    // Comma separated. Do not use commas inside a name.
    public string Categories { get; set; } = "Travel,Accommodation,Meals,Fuel,Office Supplies,Software and Subscriptions,Communication,Training,Client Entertainment,Other";
    public double Temperature { get; set; } = 0;
    public int MaxOutputTokens { get; set; } = 1800;
    public int MaxTextChars { get; set; } = 12000;
    public string? Model { get; set; }                     // empty = the default model
}

// What a queued invoice job carries. The file itself stays on disk, not in the database.
public record InvoiceJobInput(string FilePath, string FileName, string Kind, bool IncludeLineItems);

public record InvoiceSubmitResult(bool Ok, int StatusCode, string? Error, Guid JobId);

// What the AI fills in. Amounts and dates are copied as printed; the code converts them.
public class InvoiceExtraction
{
    [Description("invoice, receipt, bill or other.")]
    public string DocumentType { get; set; } = "";

    [Description("The company that issued the document (the seller), not the customer who is billed.")]
    public string VendorName { get; set; } = "";

    [Description("Tax registration number of the seller (GST, VAT, TIN, PAN...), exactly as printed. Empty if none.")]
    public string VendorTaxId { get; set; } = "";

    public string VendorAddress { get; set; } = "";

    [Description("The invoice, bill or receipt number. Not the purchase order number, a phone number or a tax id.")]
    public string InvoiceNumber { get; set; } = "";

    [Description("Invoice date exactly as printed.")]
    public string InvoiceDate { get; set; } = "";

    [Description("Due date exactly as printed. Empty if none.")]
    public string DueDate { get; set; } = "";

    public string PurchaseOrder { get; set; } = "";

    [Description("Currency code or symbol as printed, for example INR, Rs., $, EUR. Empty if none.")]
    public string Currency { get; set; } = "";

    [Description("Amount before tax, as printed without the currency symbol, for example 1,000.00. Empty if not shown.")]
    public string Subtotal { get; set; } = "";

    [Description("Tax amounts as printed. If one line shows the total tax, list only that. Otherwise list each tax amount separately.")]
    public List<string> TaxAmounts { get; set; } = new();

    [Description("Total discount as printed. Empty if none.")]
    public string Discount { get; set; } = "";

    [Description("Shipping, delivery or other extra charges as printed. Empty if none.")]
    public string Shipping { get; set; } = "";

    [Description("The final amount to pay: Total, Grand Total or Amount Due, as printed without the currency symbol.")]
    public string Total { get; set; } = "";

    [Description("One of the allowed expense categories.")]
    public string ExpenseCategory { get; set; } = "";

    public List<InvoiceLineExtraction> LineItems { get; set; } = new();
}

public class InvoiceLineExtraction
{
    public string Description { get; set; } = "";

    [Description("Quantity as printed, for example 2 or 1.5. Empty if not shown.")]
    public string Quantity { get; set; } = "";

    [Description("Price of one unit as printed, without the currency symbol. Empty if not shown.")]
    public string UnitPrice { get; set; } = "";

    [Description("Line amount as printed, without the currency symbol.")]
    public string Amount { get; set; } = "";
}

using System.Text.Json;

namespace HrmsAi.Invoices;

public static class InvoicePrompt
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static string System(string categories, bool lineItems, int maxItems)
    {
        var lineRule = lineItems
            ? $"lineItems: one entry for each product or service bought (at most {maxItems}). Do not include subtotal, tax, discount, shipping or total rows."
            : "lineItems: always return an empty list.";

        return $"""
            You read the text of an invoice or receipt and fill in the fields. The text may come from OCR, so it can contain small mistakes, and the columns may be spread out.

            Rules:
            - Copy text and amounts exactly as printed. Never invent, guess or calculate. Use an empty string when something is not in the text.
            - Amounts: copy the printed number with its separators and without the currency symbol (for example 1,180.00).
            - vendorName is the company that issued the document (the seller), not the customer who is billed.
            - invoiceNumber is the invoice, bill or receipt number. It is not the purchase order number, a phone number or a tax registration number.
            - invoiceDate and dueDate: copy them exactly as printed.
            - subtotal is the amount before tax. total is the final amount to pay (Total, Grand Total, Amount Due).
            - taxAmounts: if one line prints the total tax, list only that amount. Otherwise list each separate tax amount (for example CGST and SGST, or VAT) as its own item.
            - {lineRule}
            - expenseCategory must be exactly one of: {categories}. Use Other when unsure.
            - documentType is invoice, receipt, bill or other.
            - Return JSON only.
            """;
    }

    public const string ExampleText = """
        ACME OFFICE SUPPLIES LTD
        12 Market Road, Springfield
        Tax ID: TX-55-2210

        TAX INVOICE                          Invoice No: INV-2026-0042
        Date: 03/04/2026                     PO: PO-7781
        Bill to: Northwind Traders

        Description                Qty      Rate        Amount
        A4 Paper (box)               2    450.00        900.00
        Ink Cartridge                1  1,250.00      1,250.00

        Subtotal                                      2,150.00
        VAT 10%                                         215.00
        Total                                         2,365.00
        """;

    public static string ExampleAnswer(string[] categories, bool lineItems)
    {
        var category = categories.FirstOrDefault(c => c.Equals("Office Supplies", StringComparison.OrdinalIgnoreCase)) ?? "Other";
        var example = new InvoiceExtraction
        {
            DocumentType = "invoice",
            VendorName = "ACME OFFICE SUPPLIES LTD",
            VendorTaxId = "TX-55-2210",
            VendorAddress = "12 Market Road, Springfield",
            InvoiceNumber = "INV-2026-0042",
            InvoiceDate = "03/04/2026",
            PurchaseOrder = "PO-7781",
            Subtotal = "2,150.00",
            TaxAmounts = new List<string> { "215.00" },
            Total = "2,365.00",
            ExpenseCategory = category
        };
        if (lineItems)
        {
            example.LineItems = new List<InvoiceLineExtraction>
            {
                new() { Description = "A4 Paper (box)", Quantity = "2", UnitPrice = "450.00", Amount = "900.00" },
                new() { Description = "Ink Cartridge", Quantity = "1", UnitPrice = "1,250.00", Amount = "1,250.00" }
            };
        }
        return JsonSerializer.Serialize(example, Web);
    }
}

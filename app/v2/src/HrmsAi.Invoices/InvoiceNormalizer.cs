using System.Text.RegularExpressions;

namespace HrmsAi.Invoices;

// Builds the fixed public result from what the AI copied, and checks it for mistakes.
public static class InvoiceNormalizer
{
    public static ParsedInvoice Normalize(InvoiceExtraction x, string text, string source, int pages, InvoiceSettings s, string model)
    {
        var p = new ParsedInvoice();
        p.Meta.Model = model;
        p.Meta.ParsedAtUtc = DateTime.UtcNow;
        p.Document.Source = source;
        p.Document.Pages = pages;
        p.Document.Type = Pick(x.DocumentType, new[] { "invoice", "receipt", "bill" }) ?? "other";
        if (source == "ocr") p.Warnings.Add("This document was read with OCR. Please check the amounts.");

        p.Vendor.Name = Clean(x.VendorName);
        p.Vendor.TaxId = Clean(x.VendorTaxId);
        p.Vendor.Address = Clean(x.VendorAddress);
        p.Invoice.Number = Clean(x.InvoiceNumber);
        p.Invoice.PurchaseOrder = Clean(x.PurchaseOrder);
        if (p.Vendor.Name is null) p.Warnings.Add("The vendor name was not found.");
        if (p.Invoice.Number is null) p.Warnings.Add("The invoice number was not found.");

        // ---- dates ----
        var dayFirst = !string.Equals(s.DateOrder, "MDY", StringComparison.OrdinalIgnoreCase);
        var (date, dateText, dateWarn) = ReadDate(x.InvoiceDate, "invoice date", dayFirst);
        p.Invoice.Date = date; p.Invoice.DateText = dateText;
        if (dateWarn is not null) p.Warnings.Add(dateWarn);
        if (dateText is null) p.Warnings.Add("The invoice date was not found.");

        var (due, dueText, dueWarn) = ReadDate(x.DueDate, "due date", dayFirst);
        p.Invoice.DueDate = due; p.Invoice.DueDateText = dueText;
        if (dueWarn is not null) p.Warnings.Add(dueWarn);

        // ---- currency and category ----
        var (currency, currencyWarn) = InvoiceParsing.Currency(x.Currency, s.DefaultCurrency);
        p.Currency = currency;
        if (currencyWarn is not null) p.Warnings.Add(currencyWarn);
        if (currency is null) p.Warnings.Add("No currency was found.");

        var categories = InvoiceExtractor.Categories(s);
        p.ExpenseCategory = Pick(x.ExpenseCategory, categories) ?? (categories.Length > 0 ? "Other" : null);

        // ---- amounts ----
        var subtotal = InvoiceParsing.Amount(x.Subtotal);
        var taxParts = (x.TaxAmounts ?? new List<string>())
            .Select(InvoiceParsing.Amount).Where(a => a is not null).Select(a => a!.Value).ToList();
        decimal? tax = taxParts.Count > 0 ? taxParts.Sum() : null;
        var discount = InvoiceParsing.Amount(x.Discount);
        var shipping = InvoiceParsing.Amount(x.Shipping);
        var total = InvoiceParsing.Amount(x.Total);

        var lines = new List<InvoiceLine>();
        foreach (var li in (x.LineItems ?? new List<InvoiceLineExtraction>()).Take(s.MaxLineItems))
        {
            var description = Clean(li.Description);
            var amount = InvoiceParsing.Amount(li.Amount);
            if (description is null && amount is null) continue;
            if (description is not null &&
                Regex.IsMatch(description, @"^(sub\s*total|total|grand total|tax|gst|vat|discount|balance|amount due)\b", RegexOptions.IgnoreCase)) continue;

            var qty = InvoiceParsing.Quantity(li.Quantity);
            var unit = InvoiceParsing.Amount(li.UnitPrice);
            if (amount is null && qty is not null && unit is not null) amount = Math.Round(qty.Value * unit.Value, 2) + 0.00m;
            lines.Add(new InvoiceLine { Description = description, Quantity = qty, UnitPrice = unit, Amount = amount });
        }

        // ---- checks ----
        var withAmount = lines.Where(l => l.Amount is not null).ToList();
        if (subtotal is not null && withAmount.Count > 0)
        {
            var sum = withAmount.Sum(l => l.Amount!.Value);
            p.Checks.LineItemsMatchSubtotal = Math.Abs(sum - subtotal.Value) <= 1.0m;
            if (p.Checks.LineItemsMatchSubtotal == false)
                p.Warnings.Add($"The line items add up to {sum:N2}, but the subtotal is {subtotal:N2}. Some lines may be missing or misread.");
        }

        if (total is null && subtotal is not null)
        {
            total = subtotal.Value + (tax ?? 0) - (discount ?? 0) + (shipping ?? 0);
            total = Math.Round(total.Value, 2) + 0.00m;
            p.Warnings.Add("The total was not found and was calculated from the subtotal, tax, discount and shipping.");
        }
        else if (total is not null && subtotal is not null)
        {
            var expected = subtotal.Value + (tax ?? 0) - (discount ?? 0) + (shipping ?? 0);
            p.Checks.TotalsAddUp = Math.Abs(expected - total.Value) <= 1.0m;
            if (p.Checks.TotalsAddUp == false)
                p.Warnings.Add($"Subtotal + tax - discount + shipping gives {expected:N2}, but the total is {total:N2}.");
        }
        if (total is null) p.Warnings.Add("No total was found.");

        // a number the AI reports should really be in the document text
        var seen = new HashSet<decimal>();
        foreach (Match m in Regex.Matches(text, @"\d[\d.,]*\d|\d"))
        {
            var value = InvoiceParsing.Amount(m.Value);
            if (value is not null) seen.Add(value.Value);
        }
        if (InvoiceParsing.Amount(x.Total) is { } printedTotal && !seen.Contains(printedTotal))
            p.Warnings.Add($"The total {printedTotal:N2} was not found in the document text. Please check it.");
        if (subtotal is not null && !seen.Contains(subtotal.Value))
            p.Warnings.Add($"The subtotal {subtotal:N2} was not found in the document text. Please check it.");

        p.Totals.Subtotal = subtotal;
        p.Totals.Tax = tax;
        p.Totals.Discount = discount;
        p.Totals.Shipping = shipping;
        p.Totals.Total = total;
        p.LineItems = lines;
        return p;
    }

    private static (string? Iso, string? Text, string? Warning) ReadDate(string? raw, string what, bool dayFirst)
    {
        var text = Clean(raw);
        if (text is null) return (null, null, null);
        var (iso, ambiguous) = InvoiceParsing.Date(text, dayFirst);
        if (iso is null) return (null, text, $"The {what} '{text}' could not be read as a date.");
        if (ambiguous)
            return (iso, text, $"The {what} '{text}' can be read day-first or month-first. It was read as {(dayFirst ? "day first" : "month first")}. Please confirm it.");
        return (iso, text, null);
    }

    private static string? Clean(string? s)
    {
        var t = Regex.Replace(s ?? "", @"\s+", " ").Trim();
        return t.Length == 0 ? null : t;
    }

    private static string? Pick(string? value, IEnumerable<string> allowed)
    {
        var v = Clean(value);
        return v is null ? null : allowed.FirstOrDefault(a => a.Equals(v, StringComparison.OrdinalIgnoreCase));
    }
}

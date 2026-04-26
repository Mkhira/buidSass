using System.Globalization;
using System.Text;

namespace BackendApi.Modules.TaxInvoices.Rendering;

/// <summary>
/// Bilingual AR/EN RTL-first HTML composer (Principle 4, research R8). Razor would normally
/// compile this at build time; for Phase 1B we emit hand-coded HTML so the spec 003 PDF
/// abstraction can render directly without a Razor runtime dependency. Layout: Arabic on
/// the right (primary), English on the left (secondary) for every row. Numbers use ASCII
/// digits so KSA's tax authority can parse them.
/// </summary>
public sealed class HtmlTemplateRenderer
{
    public string Compose(InvoiceRenderModel model)
    {
        var sb = new StringBuilder(8 * 1024);
        var title = model.IsCreditNote ? "إشعار دائن / Credit Note" : "فاتورة ضريبية / Tax Invoice";
        sb.Append("""
            <!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8"/>
            <style>
              body { font-family: 'NotoNaskhArabic','Noto Naskh Arabic','Arial Unicode MS',sans-serif; font-size: 11pt; }
              .header { display:flex; justify-content:space-between; }
              .ar { text-align:right; direction:rtl; }
              .en { text-align:left; direction:ltr; }
              table { width:100%; border-collapse:collapse; margin-top:12px; }
              th,td { border:1px solid #444; padding:6px; }
              th { background:#f3f3f3; }
              .totals { margin-top:12px; width:50%; float:left; }
              .qr { float:right; margin-top:12px; }
              .footer { margin-top:24px; font-size:9pt; color:#444; }
            </style></head><body>
            """);
        sb.Append("<h1>").Append(WebEscape(title)).Append("</h1>");
        sb.Append("<div class=\"header\">");
        sb.Append("<div class=\"ar\"><strong>").Append(WebEscape(model.SellerLegalNameAr)).Append("</strong><br/>")
          .Append(WebEscape(model.SellerAddressAr)).Append("<br/>")
          .Append(WebEscape("الرقم الضريبي: ")).Append(WebEscape(model.SellerVatNumber)).Append("</div>");
        sb.Append("<div class=\"en\"><strong>").Append(WebEscape(model.SellerLegalNameEn)).Append("</strong><br/>")
          .Append(WebEscape(model.SellerAddressEn)).Append("<br/>VAT: ")
          .Append(WebEscape(model.SellerVatNumber)).Append("</div>");
        sb.Append("</div>");

        sb.Append("<p class=\"ar\"><strong>رقم الفاتورة:</strong> ").Append(WebEscape(model.InvoiceNumber)).Append("<br/>");
        sb.Append("<strong>رقم الطلب:</strong> ").Append(WebEscape(model.OrderNumber)).Append("<br/>");
        sb.Append("<strong>تاريخ الإصدار:</strong> ")
          .Append(model.IssuedAt.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))
          .Append("</p>");

        if (model.IsCreditNote && !string.IsNullOrEmpty(model.CreditNoteOriginalInvoiceNumber))
        {
            sb.Append("<p class=\"ar\"><strong>الفاتورة الأصلية:</strong> ")
              .Append(WebEscape(model.CreditNoteOriginalInvoiceNumber!)).Append("</p>");
        }
        if (!string.IsNullOrEmpty(model.B2bPoNumber))
        {
            sb.Append("<p class=\"ar\"><strong>أمر الشراء (PO):</strong> ")
              .Append(WebEscape(model.B2bPoNumber!)).Append("</p>");
        }

        sb.Append("<p class=\"ar\"><strong>المشتري / Bill To:</strong><br/>")
          .Append(WebEscape(model.BillToAr)).Append("<br/><span class=\"en\">")
          .Append(WebEscape(model.BillToEn)).Append("</span></p>");
        if (!string.IsNullOrEmpty(model.BuyerVatNumber))
        {
            sb.Append("<p class=\"ar\"><strong>الرقم الضريبي للمشتري:</strong> ")
              .Append(WebEscape(model.BuyerVatNumber!)).Append("</p>");
        }

        sb.Append("<table><thead><tr>");
        sb.Append("<th>#</th><th>SKU</th><th>الصنف / Item</th><th>الكمية / Qty</th>");
        sb.Append("<th>السعر / Unit</th><th>الخصم / Discount</th><th>الضريبة / VAT</th><th>الإجمالي / Total</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var line in model.Lines)
        {
            var sign = model.IsCreditNote ? -1 : 1;
            sb.Append("<tr>")
              .Append("<td>").Append(line.Number).Append("</td>")
              .Append("<td>").Append(WebEscape(line.Sku)).Append("</td>")
              .Append("<td><span class=\"ar\">").Append(WebEscape(line.NameAr)).Append("</span><br/><span class=\"en\">")
                  .Append(WebEscape(line.NameEn)).Append("</span></td>")
              .Append("<td>").Append(line.Qty).Append("</td>")
              .Append("<td>").Append(FormatMinor(line.UnitPriceMinor, model.Currency)).Append("</td>")
              .Append("<td>").Append(FormatMinor(sign * line.LineDiscountMinor, model.Currency)).Append("</td>")
              .Append("<td>").Append(FormatMinor(sign * line.LineTaxMinor, model.Currency))
                  .Append(" (").Append((line.TaxRateBp / 100m).ToString("0.##", CultureInfo.InvariantCulture)).Append(" %)</td>")
              .Append("<td>").Append(FormatMinor(sign * line.LineTotalMinor, model.Currency)).Append("</td>")
              .Append("</tr>");
        }
        sb.Append("</tbody></table>");

        var totalSign = model.IsCreditNote ? -1 : 1;
        sb.Append("<table class=\"totals\"><tbody>");
        AppendTotal(sb, "المجموع الفرعي / Subtotal", totalSign * model.SubtotalMinor, model.Currency);
        AppendTotal(sb, "الخصم / Discount", totalSign * model.DiscountMinor, model.Currency);
        AppendTotal(sb, "الضريبة / VAT", totalSign * model.TaxMinor, model.Currency);
        AppendTotal(sb, "الشحن / Shipping", totalSign * model.ShippingMinor, model.Currency);
        AppendTotal(sb, "الإجمالي / Grand Total", totalSign * model.GrandTotalMinor, model.Currency);
        sb.Append("</tbody></table>");

        if (!string.IsNullOrEmpty(model.ZatcaQrB64))
        {
            sb.Append("<div class=\"qr\"><strong>ZATCA QR</strong><br/><code style=\"word-break:break-all\">")
              .Append(WebEscape(model.ZatcaQrB64!)).Append("</code></div>");
        }

        if (!string.IsNullOrEmpty(model.BankNameAr) || !string.IsNullOrEmpty(model.Iban))
        {
            sb.Append("<p class=\"ar\"><strong>تفاصيل البنك / Bank Details:</strong><br/>")
              .Append(WebEscape(model.BankNameAr ?? string.Empty)).Append(" / ")
              .Append(WebEscape(model.BankNameEn ?? string.Empty)).Append("<br/>IBAN: ")
              .Append(WebEscape(model.Iban ?? string.Empty)).Append("</p>");
        }

        sb.Append("<div class=\"footer\">")
          .Append(model.FooterHtmlAr ?? string.Empty)
          .Append(model.FooterHtmlEn ?? string.Empty)
          .Append("</div>");

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendTotal(StringBuilder sb, string label, long minor, string currency)
    {
        sb.Append("<tr><th>").Append(WebEscape(label)).Append("</th><td>")
          .Append(FormatMinor(minor, currency)).Append("</td></tr>");
    }

    private static string FormatMinor(long minor, string currency)
    {
        var integerPart = minor / 100;
        var fractional = Math.Abs(minor % 100);
        return string.Create(CultureInfo.InvariantCulture,
            $"{integerPart}.{fractional:D2} {currency}");
    }

    private static string WebEscape(string s) =>
        System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
}

using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OnwardsSwift.API.Services
{
    public static class InvoicePdfGenerator
    {
        public static byte[] Generate(
            string clientName,
            string clientEmail,
            string clientPhone,
            DateTime from,
            DateTime to,
            IEnumerable<InvoicePdfLine> lines)
        {
            var rows = lines?.ToList() ?? new List<InvoicePdfLine>();
            var total = rows.Sum(x => x.Amount);
            var sampleInvoice = rows.FirstOrDefault()?.InvoiceNumber ?? "N/A";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);

                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("ONWARDS SWIFT").SemiBold().FontSize(14);
                            });

                            r.ConstantItem(220).Column(c =>
                            {
                                c.Item().AlignRight().Text("INVOICE").FontSize(22).SemiBold().FontColor(Colors.Blue.Medium);
                                c.Item().AlignRight().Text($"Invoice No: {sampleInvoice}");
                                c.Item().AlignRight().Text($"Date: {DateTime.Today:dd-MMM-yyyy}");
                                c.Item().AlignRight().Text($"Period: {from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}").FontSize(8);
                            });
                        });

                        col.Item().Border(1).Padding(6).Column(c =>
                        {
                            c.Item().Text(clientName ?? string.Empty).SemiBold();
                            c.Item().Text($"Phone: {clientPhone}");
                            c.Item().Text($"Email: {clientEmail}");
                        });

                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(4);
                                columns.RelativeColumn(2);
                                columns.ConstantColumn(40);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(80);
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Product Descriptions").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Item Descriptions").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Qty").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("Unit Price").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("Amount").SemiBold();
                            });

                            foreach (var row in rows)
                            {
                                table.Cell().Element(c => c.Border(1).Padding(4)).Text($"INVOICE {row.InvoiceNumber}");
                                table.Cell().Element(c => c.Border(1).Padding(4)).Text(string.IsNullOrWhiteSpace(row.Description) ? "CHARGES" : row.Description);
                                table.Cell().Element(c => c.Border(1).Padding(4)).Text(row.Quantity.ToString("0.##"));
                                table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(row.UnitPrice.ToString("N2"));
                                table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(row.Amount.ToString("N2")).SemiBold();
                            }
                        });

                        col.Item().AlignRight().Width(220).PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.ConstantColumn(100);
                            });

                            table.Cell().Element(c => c.Border(1).Padding(4)).Text("Sub Total").SemiBold();
                            table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(total.ToString("N2")).SemiBold();

                            table.Cell().Element(c => c.Border(1).Padding(4)).Text("Tax Rate");
                            table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("0.00");

                            table.Cell().Element(c => c.Border(1).Padding(4)).Text("Tax");
                            table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("0.00");

                            table.Cell().Element(c => c.Border(1).Padding(4)).Text("Total").SemiBold();
                            table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(total.ToString("N2")).SemiBold();
                        });
                    });

                    page.Footer().AlignCenter().Text("*** End Of Report ***").FontSize(8);
                });
            });

            return doc.GeneratePdf();
        }
    }
}

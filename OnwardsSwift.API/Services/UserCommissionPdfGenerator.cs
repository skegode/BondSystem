using System;
using System.Collections.Generic;
using System.Linq;
using OnwardsSwift.Core.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OnwardsSwift.API.Services
{
    public static class UserCommissionPdfGenerator
    {
        private static IContainer HeaderCell(IContainer c) =>
            c.Border(1).BorderColor(Colors.Grey.Lighten2)
             .Background(Colors.Blue.Darken3)
             .PaddingVertical(5).PaddingHorizontal(4);

        private static IContainer SubHeaderCell(IContainer c) =>
            c.Border(1).BorderColor(Colors.Grey.Lighten2)
             .Background(Colors.Grey.Lighten3)
             .PaddingVertical(4).PaddingHorizontal(4);

        private static IContainer DataCell(IContainer c) =>
            c.Border(1).BorderColor(Colors.Grey.Lighten2)
             .PaddingVertical(4).PaddingHorizontal(4);

        private static IContainer TotalCell(IContainer c) =>
            c.Border(1).BorderColor(Colors.Grey.Lighten2)
             .Background(Colors.Grey.Lighten4)
             .PaddingVertical(4).PaddingHorizontal(4);

        public static byte[] Generate(
            UserCommissionReportViewModel model,
            DateTime? from,
            DateTime? to,
            string? filterUserName,
            string? filterProductType,
            byte[]? logoBytes = null)
        {
            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    // ── Header ──────────────────────────────────────────────
                    page.Header().Height(70).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            if (logoBytes != null && logoBytes.Length > 0)
                                col.Item().Height(54).Image(logoBytes).FitHeight();
                            else
                                col.Item().Text("ONWARDS SWIFT").FontSize(16).SemiBold().FontColor(Colors.Blue.Darken3);

                            col.Item().PaddingTop(4).Text("User Commission Report").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(220).AlignRight().Column(col =>
                        {
                            var period = from.HasValue && to.HasValue
                                ? $"{from.Value:dd MMM yyyy} – {to.Value:dd MMM yyyy}"
                                : "All dates";
                            col.Item().AlignRight().Text($"Period: {period}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(filterUserName))
                                col.Item().AlignRight().Text($"User: {filterUserName}").FontSize(9);
                            if (!string.IsNullOrWhiteSpace(filterProductType))
                                col.Item().AlignRight().Text($"Product: {filterProductType}").FontSize(9);
                            col.Item().AlignRight().Text($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    page.Content().PaddingTop(4).Column(col =>
                    {
                        col.Spacing(10);

                        // ── Summary cards ────────────────────────────────────
                        col.Item().Row(r =>
                        {
                            SummaryCard(r, "Total Applications", model.TotalApplications.ToString());
                            SummaryCard(r, "Commission Base", $"KES {model.TotalCommissionBase:N2}");
                            SummaryCard(r, "Total Commission Due", $"KES {model.TotalCommission:N2}");
                        });

                        // ── User Summary Table ───────────────────────────────
                        col.Item().Text("Summary by User").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken3);

                        col.Item().Element(e =>
                        {
                            e.Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(3);       // User
                                    cols.ConstantColumn(50);      // %
                                    cols.ConstantColumn(55);      // Apps
                                    cols.RelativeColumn(2);       // Client Charges
                                    cols.RelativeColumn(2);       // Bank Charges
                                    cols.RelativeColumn(2);       // Commission Base
                                    cols.RelativeColumn(2);       // Commission Amount
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Element(HeaderCell).Text("User").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignCenter().Text("Comm %").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignCenter().Text("Applications").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Client Charges").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Bank Charges").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Comm. Base").FontSize(9).SemiBold().FontColor(Colors.White);
                                    h.Cell().Element(HeaderCell).AlignRight().Text("Commission Due").FontSize(9).SemiBold().FontColor(Colors.White);
                                });

                                foreach (var s in model.Summary)
                                {
                                    table.Cell().Element(DataCell).Text(s.UserName).FontSize(9);
                                    table.Cell().Element(DataCell).AlignCenter().Text($"{s.CommissionPercent:0.##}%").FontSize(9);
                                    table.Cell().Element(DataCell).AlignCenter().Text(s.Applications.ToString()).FontSize(9);
                                    table.Cell().Element(DataCell).AlignRight().Text(s.ClientCharges.ToString("N2")).FontSize(9);
                                    table.Cell().Element(DataCell).AlignRight().Text(s.BankCharges.ToString("N2")).FontSize(9);
                                    table.Cell().Element(DataCell).AlignRight().Text(s.CommissionBase.ToString("N2")).FontSize(9);
                                    table.Cell().Element(DataCell).AlignRight().Text(s.CommissionAmount.ToString("N2")).FontSize(9).SemiBold();
                                }

                                // Totals row
                                table.Cell().Element(TotalCell).Text("TOTAL").FontSize(9).SemiBold();
                                table.Cell().Element(TotalCell).Text(string.Empty);
                                table.Cell().Element(TotalCell).AlignCenter().Text(model.TotalApplications.ToString()).FontSize(9).SemiBold();
                                table.Cell().Element(TotalCell).AlignRight().Text(model.Summary.Sum(x => x.ClientCharges).ToString("N2")).FontSize(9).SemiBold();
                                table.Cell().Element(TotalCell).AlignRight().Text(model.Summary.Sum(x => x.BankCharges).ToString("N2")).FontSize(9).SemiBold();
                                table.Cell().Element(TotalCell).AlignRight().Text(model.TotalCommissionBase.ToString("N2")).FontSize(9).SemiBold();
                                table.Cell().Element(TotalCell).AlignRight().Text(model.TotalCommission.ToString("N2")).FontSize(9).SemiBold();
                            });
                        });

                        // ── Detailed Transactions ────────────────────────────
                        if (model.Details.Any())
                        {
                            col.Item().Text("Transaction Detail").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken3);

                            col.Item().Element(e =>
                            {
                                e.Table(table =>
                                {
                                    table.ColumnsDefinition(cols =>
                                    {
                                        cols.ConstantColumn(65);       // Date
                                        cols.ConstantColumn(70);       // Reference
                                        cols.RelativeColumn(2);        // Client
                                        cols.RelativeColumn(2);        // Procuring Entity
                                        cols.RelativeColumn(2);        // User
                                        cols.ConstantColumn(55);       // Product
                                        cols.ConstantColumn(50);       // Comm %
                                        cols.RelativeColumn();         // Comm. Base
                                        cols.RelativeColumn();         // Commission
                                    });

                                    table.Header(h =>
                                    {
                                        h.Cell().Element(SubHeaderCell).Text("Date").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).Text("Reference").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).Text("Client").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).Text("Procuring Entity").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).Text("User").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).Text("Product").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).AlignCenter().Text("Comm %").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).AlignRight().Text("Base").FontSize(8).SemiBold();
                                        h.Cell().Element(SubHeaderCell).AlignRight().Text("Commission").FontSize(8).SemiBold();
                                    });

                                    foreach (var d in model.Details)
                                    {
                                        var product = d.FacilityType switch
                                        {
                                            1 => "Bid Bond",
                                            2 => "Perf. Bond",
                                            3 => "Adv. Payment",
                                            _ => d.FacilityType.ToString()
                                        };

                                        table.Cell().Element(DataCell).Text(d.ApplicationDate.ToString("dd-MM-yyyy")).FontSize(8);
                                        table.Cell().Element(DataCell).Text(d.Reference).FontSize(8);
                                        table.Cell().Element(DataCell).Text(d.ClientName).FontSize(8);
                                        table.Cell().Element(DataCell).Text(d.ProcuringEntity).FontSize(8);
                                        table.Cell().Element(DataCell).Text(d.UserName).FontSize(8);
                                        table.Cell().Element(DataCell).Text(product).FontSize(8);
                                        table.Cell().Element(DataCell).AlignCenter().Text($"{d.CommissionPercent:0.##}%").FontSize(8);
                                        table.Cell().Element(DataCell).AlignRight().Text(d.CommissionBase.ToString("N2")).FontSize(8);
                                        table.Cell().Element(DataCell).AlignRight().Text(d.CommissionAmount.ToString("N2")).FontSize(8).SemiBold();
                                    }
                                });
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                        x.Span($"   |   Generated {DateTime.Now:dd MMM yyyy HH:mm}");
                    });
                });
            });

            return doc.GeneratePdf();
        }

        private static void SummaryCard(RowDescriptor row, string label, string value)
        {
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
               .Background(Colors.Grey.Lighten4)
               .Padding(8)
               .Column(col =>
               {
                   col.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                   col.Item().Text(value).FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3);
               });
        }
    }
}

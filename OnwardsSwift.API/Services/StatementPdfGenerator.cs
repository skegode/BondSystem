using System;
using System.Collections.Generic;
using OnwardsSwift.Core.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace OnwardsSwift.API.Services
{
    public static class StatementPdfGenerator
    {
        public static byte[] Generate(
            string clientName,
            string clientEmail,
            string clientPhone,
            IEnumerable<StatementLine> lines,
            DateTime from,
            DateTime to,
            decimal broughtForward,
            decimal totalDue)
        {
            var ordered = lines;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(10));

                    page.Header().Height(60).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("ONWARDS SWIFT").FontSize(16).SemiBold();
                            col.Item().Text("Client Statement").FontSize(10).FontColor(Colors.Grey.Darken1);
                        });

                        row.ConstantItem(200).Column(col =>
                        {
                            col.Item().AlignRight().Text($"Period: {from:yyyy-MM-dd} to {to:yyyy-MM-dd}").FontSize(9);
                            col.Item().AlignRight().Text($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Lighten2);
                        });
                    });

                    page.Content().PaddingVertical(5).Column(col =>
                    {
                        col.Spacing(6);

                        col.Item().Row(r =>
                        {
                                r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(clientName ?? string.Empty).Bold();
                                c.Item().Text(clientEmail ?? string.Empty).FontSize(9);
                                c.Item().Text(clientPhone ?? string.Empty).FontSize(9);
                            });

                            r.ConstantItem(150).AlignRight().Column(c =>
                            {
                                c.Item().Text($"Brought Forward: {broughtForward:N2}");
                                c.Item().Text($"Total Due: {totalDue:N2}").SemiBold();
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().Element(e =>
                        {
                            e.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(70);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(80);
                                });

                                // Header
                                table.Header(header =>
                                {
                                    header.Cell().Element(cell => cell.Padding(4)).Text("Date").FontSize(9).SemiBold();
                                    header.Cell().Element(cell => cell.Padding(4)).Text("Description").FontSize(9).SemiBold();
                                    header.Cell().Element(cell => cell.Padding(4)).AlignRight().Text("Debit").FontSize(9).SemiBold();
                                    header.Cell().Element(cell => cell.Padding(4)).AlignRight().Text("Credit").FontSize(9).SemiBold();
                                    header.Cell().Element(cell => cell.Padding(4)).AlignRight().Text("Balance").FontSize(9).SemiBold();
                                });

                                // Body
                                decimal lastBalance = 0;
                                foreach (var ln in ordered)
                                {
                                    var date = ln.TransactionDate;
                                    var desc = ln.Description ?? string.Empty;
                                    var debit = ln.Debit;
                                    var credit = ln.Credit;
                                    var bal = ln.RunningBalance;

                                    table.Cell().Element(cell => cell.Padding(4)).Text(date.ToString("yyyy-MM-dd")).FontSize(9);
                                    table.Cell().Element(cell => cell.Padding(4)).Text(desc).FontSize(9);
                                    table.Cell().Element(cell => cell.Padding(4)).AlignRight().Text(debit != 0 ? debit.ToString("N2") : string.Empty).FontSize(9);
                                    table.Cell().Element(cell => cell.Padding(4)).AlignRight().Text(credit != 0 ? credit.ToString("N2") : string.Empty).FontSize(9);
                                    table.Cell().Element(cell => cell.Padding(4)).AlignRight().Text(bal.ToString("N2")).FontSize(9);

                                    lastBalance = bal;
                                }
                            });
                        });

                    });

                    page.Footer().AlignCenter().Text(x => x.Span($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}"));
                });
            });

            return doc.GeneratePdf();
        }
    }
}

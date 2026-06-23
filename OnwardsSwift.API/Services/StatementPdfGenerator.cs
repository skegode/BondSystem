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
        private static IContainer HeaderCell(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .Background(Colors.Grey.Lighten4)
                .PaddingVertical(6)
                .PaddingHorizontal(4);
        }

        private static IContainer DataCell(IContainer container)
        {
            return container
                .Border(1)
                .BorderColor(Colors.Grey.Lighten2)
                .PaddingVertical(5)
                .PaddingHorizontal(4);
        }

        public static byte[] Generate(
            string clientName,
            string contactPerson,
            string clientEmail,
            string clientPhone,
            string kraPin,
            string businessRegNumber,
            string physicalAddress,
            string postalAddress,
            IEnumerable<StatementLine> lines,
            DateTime from,
            DateTime to,
            decimal broughtForward,
            decimal totalDue,
            byte[]? logoBytes = null)
        {
            var ordered = lines;

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(24);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    // background watermark removed

                    page.Header().Column(header =>
                    {
                        header.Item().Row(row =>
                        {
                            row.RelativeItem().Column(col =>
                            {
                                if (logoBytes != null && logoBytes.Length > 0)
                                    col.Item().Height(54).Image(logoBytes).FitHeight();
                                else
                                    col.Item().Text("ONWARDS SWIFT").FontSize(18).SemiBold();

                                col.Item().PaddingTop(4).Text("Client Statement").FontSize(10).FontColor(Colors.Grey.Darken1);
                            });

                            row.ConstantItem(220).AlignRight().Column(col =>
                            {
                                col.Item().Text("IPS Building, 4th Floor").FontSize(9);
                                col.Item().Text("Kimathi Street, Nairobi CBD").FontSize(9);
                                col.Item().Text("P.O Box 104322-00100 NRB").FontSize(9);
                                col.Item().Text("Website: onwardsswift.com").FontSize(9);
                                col.Item().PaddingTop(4).Text($"Period: {from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}").FontSize(8.5f).SemiBold();
                                col.Item().Text($"Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        header.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingVertical(5).Column(col =>
                    {
                        col.Spacing(10);

                        col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Statement For").FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
                                c.Item().Text(clientName ?? string.Empty).FontSize(13).Bold();

                                void AddDetailRow(string leftLabel, string leftValue, string rightLabel, string rightValue)
                                {
                                    c.Item().PaddingTop(6).Row(detailRow =>
                                    {
                                        detailRow.RelativeItem().Column(detail =>
                                        {
                                            detail.Item().Text(leftLabel).FontSize(7).FontColor(Colors.Grey.Darken1).SemiBold();
                                            detail.Item().Text(string.IsNullOrWhiteSpace(leftValue) ? "-" : leftValue).FontSize(8.5f);
                                        });

                                        detailRow.ConstantItem(12).Text(string.Empty);

                                        detailRow.RelativeItem().Column(detail =>
                                        {
                                            detail.Item().Text(rightLabel).FontSize(7).FontColor(Colors.Grey.Darken1).SemiBold();
                                            detail.Item().Text(string.IsNullOrWhiteSpace(rightValue) ? "-" : rightValue).FontSize(8.5f);
                                        });
                                    });
                                }

                                AddDetailRow("Contact Person", contactPerson, "Phone", clientPhone);
                                AddDetailRow("Email", clientEmail, "KRA PIN", kraPin);
                                AddDetailRow("Business Registration No", businessRegNumber, "Postal Address", postalAddress);
                                AddDetailRow("Physical Address", physicalAddress, "", "");
                            });

                            r.ConstantItem(150).AlignRight().Column(c =>
                            {
                                c.Item().Text("Balance Summary").FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
                                c.Item().PaddingTop(8).Text($"Brought Forward: {broughtForward:N2}").FontSize(9);
                                c.Item().Text($"Closing Balance: {totalDue:N2}").FontSize(11).SemiBold();
                            });
                        });

                        col.Item().Element(e =>
                        {
                            e.Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(50);
                                    columns.RelativeColumn(1.3f);
                                    columns.RelativeColumn(1.0f);
                                    columns.ConstantColumn(52);
                                    columns.ConstantColumn(52);
                                    columns.ConstantColumn(58);
                                    columns.ConstantColumn(46);
                                    columns.ConstantColumn(46);
                                    columns.ConstantColumn(54);
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(HeaderCell).Text("Date").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).Text("Product Item").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).Text("Procuring Entity").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Bond Amt").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Cash Cover").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).Text("Ref No").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Debit").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Credit").FontSize(8).SemiBold();
                                    header.Cell().Element(HeaderCell).AlignRight().Text("Balance").FontSize(8).SemiBold();
                                });

                                foreach (var ln in ordered)
                                {
                                    var rowBackground = ln.IsPaymentLine ? Colors.Blue.Lighten5 : Colors.White;

                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).Text(ln.TransactionDate.ToString("dd-MMM-yy")).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).Text(ln.IsPaymentLine ? $"  {ln.ProductItem}" : ln.ProductItem).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).Text(string.IsNullOrWhiteSpace(ln.ProcuringEntity) ? "-" : ln.ProcuringEntity).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).AlignRight().Text(ln.BondAmount != 0 ? ln.BondAmount.ToString("N2") : string.Empty).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).AlignRight().Text(ln.CashCoverAmount != 0 ? ln.CashCoverAmount.ToString("N2") : string.Empty).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).Text(ln.ReferenceNo ?? string.Empty).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).AlignRight().Text(ln.Debit != 0 ? ln.Debit.ToString("N2") : string.Empty).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).AlignRight().Text(ln.Credit != 0 ? ln.Credit.ToString("N2") : string.Empty).FontSize(8);
                                    table.Cell().Element(c => DataCell(c).Background(rowBackground)).AlignRight().Text(ln.RunningBalance.ToString("N2")).FontSize(8).SemiBold();
                                }
                            });
                        });

                        col.Item().PaddingTop(16).Row(row =>
                        {
                            row.RelativeItem().Column(signature =>
                            {
                                signature.Item().Text("Signature").FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
                                signature.Item().PaddingTop(28).LineHorizontal(1).LineColor(Colors.Black);
                                signature.Item().PaddingTop(4).Text("Authorized Signature").FontSize(8);
                            });

                            row.ConstantItem(40).Text(string.Empty);

                            row.RelativeItem().Column(dateCol =>
                            {
                                dateCol.Item().Text("Date").FontSize(8).FontColor(Colors.Grey.Darken1).SemiBold();
                                dateCol.Item().PaddingTop(28).LineHorizontal(1).LineColor(Colors.Black);
                                dateCol.Item().PaddingTop(4).Text("Date Signed").FontSize(8);
                            });
                        });

                    });

                    page.Footer().AlignCenter().Text(x => x.Span($"Generated {DateTime.Now:dd-MMM-yyyy HH:mm}"));
                });
            });

            return doc.GeneratePdf();
        }
    }
}

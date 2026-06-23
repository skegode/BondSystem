using System;
using System.Collections.Generic;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;

namespace OnwardsSwift.API.Services
{
    public static class InvoicePdfGenerator
    {
        public static byte[] Generate(
            string clientName,
            string clientContactPerson,
            string clientEmail,
            string clientPhone,
            string clientKraPin,
            string clientBusinessRegNumber,
            string clientPhysicalAddress,
            string clientPostalAddress,
            DateTime from,
            DateTime to,
            IEnumerable<InvoicePdfLine> lines,
            byte[]? logoBytes = null)
        {
            var rows = lines?.ToList() ?? new List<InvoicePdfLine>();
            var totalCharged = rows.Sum(x => x.ChargedAmount);
            var totalPaid = rows.Sum(x => x.PaidAmount);
            var totalOutstanding = rows.Sum(x => x.OutstandingAmount);
            var sampleInvoice = rows.FirstOrDefault()?.InvoiceNumber ?? "N/A";

            var doc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(9));

                    // Add a faint logo watermark in the page background to match preview branding
                    if (logoBytes != null && logoBytes.Length > 0)
                    {
                        // make watermark slightly stronger so it's visible on exports
                        var faded = CreateFadedImage(logoBytes, 0.15f);
                        if (faded != null)
                        {
                            page.Background().AlignCenter().AlignMiddle().Element(bg => bg.Width(420).Image(faded).FitWidth());
                        }
                    }

                    page.Content().Column(col =>
                    {
                        col.Spacing(6);

                        col.Item().Row(r =>
                        {
                            r.RelativeItem().AlignTop().Column(c =>
                            {
                                if (logoBytes != null && logoBytes.Length > 0)
                                    c.Item().Height(58).Image(logoBytes).FitHeight();
                                else
                                    c.Item().Text("ONWARDS SWIFT").SemiBold().FontSize(14);
                            });

                            r.ConstantItem(250).AlignTop().Column(c =>
                            {
                                c.Item().AlignRight().Text("IPS Building, 4th Floor").FontSize(8);
                                c.Item().AlignRight().Text("Kimathi Street, Nairobi CBD").FontSize(8);
                                c.Item().AlignRight().Text("P.O Box 104322-00100 NRB").FontSize(8);
                                c.Item().AlignRight().Text("Website: onwardsswift.com").FontSize(8);
                                c.Item().PaddingTop(2);
                                c.Item().AlignRight().Text("INVOICE").FontSize(22).SemiBold().FontColor(Colors.Blue.Medium);
                                c.Item().AlignRight().Text($"Invoice No: {sampleInvoice}");
                                c.Item().AlignRight().Text($"Date: {DateTime.Today:dd-MMM-yyyy}");
                                c.Item().AlignRight().Text($"Period: {from:dd-MMM-yyyy} to {to:dd-MMM-yyyy}").FontSize(8);
                            });
                        });

                        col.Item().Border(1).Padding(6).Column(c =>
                        {
                            c.Item().Text("Invoice For").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(clientName ?? string.Empty).SemiBold();
                            if (!string.IsNullOrWhiteSpace(clientContactPerson)) c.Item().Text($"Contact Person: {clientContactPerson}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientEmail)) c.Item().Text($"Email: {clientEmail}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientPhone)) c.Item().Text($"Phone: {clientPhone}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientKraPin)) c.Item().Text($"KRA PIN: {clientKraPin}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientBusinessRegNumber)) c.Item().Text($"Business Reg No: {clientBusinessRegNumber}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientPhysicalAddress)) c.Item().Text($"Physical Address: {clientPhysicalAddress}").FontSize(8);
                            if (!string.IsNullOrWhiteSpace(clientPostalAddress)) c.Item().Text($"Postal Address: {clientPostalAddress}").FontSize(8);
                        });

                        // Summary boxes (Charged / Paid / Outstanding) to match on-screen preview
                        col.Item().PaddingTop(6).Row(rSum =>
                        {
                            rSum.RelativeItem().Element(e => e.Border(1).Padding(8).Background(Colors.White).Column(cs =>
                            {
                                cs.Item().Text("Charged").FontSize(8).FontColor(Colors.Grey.Darken2);
                                cs.Item().Text($"KES {totalCharged:N2}").SemiBold().FontSize(10);
                            }));

                            rSum.RelativeItem().Element(e => e.Border(1).Padding(8).Background(Colors.White).Column(cs =>
                            {
                                cs.Item().Text("Paid").FontSize(8).FontColor(Colors.Grey.Darken2);
                                cs.Item().Text($"KES {totalPaid:N2}").SemiBold().FontSize(10).FontColor(Colors.Green.Darken2);
                            }));

                            rSum.RelativeItem().Element(e => e.Border(1).Padding(8).Background(Colors.White).Column(cs =>
                            {
                                cs.Item().Text("Outstanding").FontSize(8).FontColor(Colors.Grey.Darken2);
                                cs.Item().Text($"KES {totalOutstanding:N2}").SemiBold().FontSize(10).FontColor(Colors.Red.Medium);
                            }));
                        });

                        col.Item().PaddingTop(4).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90); // Date
                                columns.ConstantColumn(95); // Reference
                                columns.RelativeColumn(2); // Product item
                                columns.RelativeColumn(3); // Procuring Entity
                                columns.ConstantColumn(90); // Bond Amount
                                columns.ConstantColumn(100); // Outstanding
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Date").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Reference").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Product Item").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).Text("Procuring Entity").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("Bond Amount").SemiBold();
                                header.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text("Outstanding").SemiBold();
                            });

                            if (!rows.Any())
                            {
                                table.Cell().ColumnSpan(6).Element(c => c.Border(1).Padding(8)).AlignCenter().Text("No invoice lines found for the selected period.").FontColor(Colors.Grey.Darken1);
                            }
                            else
                            {
                                foreach (var row in rows)
                                {
                                    table.Cell().Element(c => c.Border(1).Padding(4)).Text(row.InvoiceDate.ToString("dd-MMM-yyyy"));
                                    table.Cell().Element(c => c.Border(1).Padding(4)).Text($"REF {row.InvoiceNumber}");
                                    table.Cell().Element(c => c.Border(1).Padding(4)).Text(string.IsNullOrWhiteSpace(row.ProductItem) ? "-" : row.ProductItem);
                                    table.Cell().Element(c => c.Border(1).Padding(4)).Text(string.IsNullOrWhiteSpace(row.Description) ? "N/A" : row.Description);
                                    table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(row.Quantity.ToString("N2"));
                                    table.Cell().Element(c => c.Border(1).Padding(4)).AlignRight().Text(row.OutstandingAmount.ToString("N2")).SemiBold();
                                }
                            }
                        });

                        // Totals row under the main table to match preview (TOTALS label + Outstanding)
                        col.Item().PaddingTop(6).Table(tot =>
                        {
                            tot.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(90);
                                columns.ConstantColumn(110);
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(100);
                            });

                            tot.Cell().ColumnSpan(3).Element(c => c.Border(0).PaddingTop(6)).AlignRight().Text("TOTALS").SemiBold();
                            tot.Cell().Element(c => c.Border(0).PaddingTop(6)).AlignRight().Text(totalOutstanding.ToString("N2")).SemiBold();
                        });

                        // Payment details section placed directly after the table and totals
                        col.Item().PaddingTop(12).Element(e => e.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(pc =>
                        {
                            pc.Spacing(4);
                            pc.Item().Text("Payment Details").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken2);
                            pc.Item().Text("Account Name: ONWARDS SWIFT COMPANY LIMITED").FontSize(9);
                            pc.Item().Text("Account No: 9783894509").FontSize(9);
                            pc.Item().Text("Bank Code: 61").FontSize(9);
                            pc.Item().Text("Branch Code: 200").FontSize(9);
                            pc.Item().Text("Swift: HFCOKENA").FontSize(9);
                            pc.Item().Text("Paybill: 100400").FontSize(9);
                            pc.Item().Text("Acc No: OnwardsSwift").FontSize(9);
                        }));
                    });

                    page.Footer().Element(f => f.AlignCenter().Text("*** End Of Report ***").FontSize(8));
                });
            });

            return doc.GeneratePdf();
        }

        private static byte[]? CreateFadedImage(byte[] srcBytes, float alpha)
        {
            try
            {
                using var inMs = new MemoryStream(srcBytes);
                using var src = System.Drawing.Image.FromStream(inMs);
                var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    var cm = new ColorMatrix
                    {
                        Matrix00 = 1,
                        Matrix11 = 1,
                        Matrix22 = 1,
                        Matrix33 = alpha,
                        Matrix44 = 1
                    };
                    var ia = new ImageAttributes();
                    ia.SetColorMatrix(cm);
                    g.Clear(System.Drawing.Color.Transparent);
                    g.DrawImage(src, new Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, src.Width, src.Height, System.Drawing.GraphicsUnit.Pixel, ia);
                }

                using var outMs = new MemoryStream();
                bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                return outMs.ToArray();
            }
            catch
            {
                return null;
            }
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Reports;

internal static class GstReturnBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static async Task<GstReturnExportDto> BuildGstr1Async(
        IUnitOfWork uow,
        ISettingsService settings,
        IQueryable<Sale> salesQuery,
        DateTime from,
        DateTime to,
        CancellationToken ct)
    {
        var (start, end) = (from.Date, to.Date.AddDays(1));
        var company = await settings.GetCompanyProfileAsync(ct);
        var gstin = NormalizeGstin(company?.GstNumber);
        var posDefault = StateCodeFromGstin(gstin) ?? "07";
        var fp = to.ToString("MMyyyy", CultureInfo.InvariantCulture);

        var sales = await salesQuery
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Include(s => s.Customer)
            .Include(s => s.Items).ThenInclude(i => i.Medicine)
            .OrderBy(s => s.InvoiceDate)
            .ToListAsync(ct);

        var returns = await uow.Repository<SaleReturn>().Query()
            .Where(r => r.Status == SaleReturnStatus.Completed && r.ReturnDate >= start && r.ReturnDate < end)
            .Include(r => r.Customer)
            .Include(r => r.Sale)
                .ThenInclude(s => s!.Customer)
            .Include(r => r.Items).ThenInclude(i => i.Medicine)
            .OrderBy(r => r.ReturnDate)
            .ToListAsync(ct);

        var preview = new List<GstReturnPreviewRowDto>();
        var b2bByCtin = new Dictionary<string, Gstr1B2bParty>(StringComparer.OrdinalIgnoreCase);
        var b2csAgg = new Dictionary<(string Pos, decimal Rate, string Supply), Gstr1B2csRow>();
        var hsnAgg = new Dictionary<(string Hsn, decimal Rate), HsnAgg>();
        var cdnrByCtin = new Dictionary<string, Gstr1CdnrParty>(StringComparer.OrdinalIgnoreCase);

        foreach (var sale in sales)
        {
            var partyGstin = NormalizeGstin(sale.Customer?.GstNumber);
            var isB2b = partyGstin.Length == 15;
            var pos = isB2b ? StateCodeFromGstin(partyGstin) ?? posDefault : posDefault;
            var interstate = sale.IgstAmount > 0;
            var invoiceValue = sale.GrandTotal;
            var partyName = sale.Customer?.Name ?? sale.BillingCustomerName ?? "Walk-in";
            var rateRows = CollapseLines(sale.Items.Select(i =>
                ToLine(i.GstPercent, i.TaxableAmount, i.TaxAmount, i.Medicine?.HsnCode, interstate, i.Quantity)));

            if (isB2b)
            {
                if (!b2bByCtin.TryGetValue(partyGstin, out var party))
                {
                    party = new Gstr1B2bParty { ctin = partyGstin, inv = [] };
                    b2bByCtin[partyGstin] = party;
                }

                party.inv.Add(new Gstr1Invoice
                {
                    inum = sale.InvoiceNumber,
                    idt = sale.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                    val = Round2(invoiceValue),
                    pos = pos,
                    rchrg = "N",
                    inv_typ = "R",
                    itms = CollapseByRate(rateRows).Select((r, idx) => new Gstr1Item
                    {
                        num = idx + 1,
                        itm_det = new Gstr1ItemDet
                        {
                            rt = r.Rate,
                            txval = r.Taxable,
                            iamt = r.Igst,
                            camt = r.Cgst,
                            samt = r.Sgst,
                            csamt = 0
                        }
                    }).ToList()
                });
            }
            else
            {
                foreach (var r in rateRows)
                {
                    var supply = interstate ? "INTER" : "INTRA";
                    var key = (pos, r.Rate, supply);
                    if (!b2csAgg.TryGetValue(key, out var row))
                    {
                        row = new Gstr1B2csRow { sply_ty = supply, rt = r.Rate, typ = "OE", pos = pos };
                        b2csAgg[key] = row;
                    }
                    row.txval += r.Taxable;
                    row.iamt += r.Igst;
                    row.camt += r.Cgst;
                    row.samt += r.Sgst;
                }
            }

            foreach (var r in rateRows)
            {
                preview.Add(new GstReturnPreviewRowDto(
                    isB2b ? "B2B" : "B2CS",
                    sale.InvoiceNumber,
                    sale.InvoiceDate,
                    partyName,
                    string.IsNullOrEmpty(partyGstin) ? null : partyGstin,
                    pos,
                    r.Rate,
                    r.Taxable,
                    r.Cgst,
                    r.Sgst,
                    r.Igst,
                    invoiceValue,
                    r.Hsn));
                AddHsn(hsnAgg, r);
            }
        }

        foreach (var ret in returns)
        {
            var partyGstin = NormalizeGstin(ret.Customer?.GstNumber ?? ret.Sale?.Customer?.GstNumber);
            if (partyGstin.Length != 15) continue;

            var pos = StateCodeFromGstin(partyGstin) ?? posDefault;
            var interstate = ret.Sale is { IgstAmount: > 0 };
            var partyName = ret.Customer?.Name ?? ret.Sale?.Customer?.Name ?? ret.Sale?.BillingCustomerName ?? "Customer";
            var rateRows = CollapseLines(ret.Items.Select(i =>
                ToLine(i.GstPercent, i.TaxableAmount, i.TaxAmount, i.Medicine?.HsnCode, interstate, i.ReturnedQuantity)));

            if (!cdnrByCtin.TryGetValue(partyGstin, out var party))
            {
                party = new Gstr1CdnrParty { ctin = partyGstin, nt = [] };
                cdnrByCtin[partyGstin] = party;
            }

            party.nt.Add(new Gstr1CreditNote
            {
                ntty = "C",
                nt_num = ret.ReturnNumber,
                nt_dt = ret.ReturnDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                inum = ret.Sale?.InvoiceNumber ?? "",
                idt = ret.Sale?.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "",
                val = Round2(ret.GrandTotal),
                pos = pos,
                rchrg = "N",
                inv_typ = "R",
                itms = CollapseByRate(rateRows).Select((r, idx) => new Gstr1Item
                {
                    num = idx + 1,
                    itm_det = new Gstr1ItemDet
                    {
                        rt = r.Rate,
                        txval = r.Taxable,
                        iamt = r.Igst,
                        camt = r.Cgst,
                        samt = r.Sgst,
                        csamt = 0
                    }
                }).ToList()
            });

            foreach (var r in rateRows)
            {
                preview.Add(new GstReturnPreviewRowDto(
                    "CDNR",
                    ret.ReturnNumber,
                    ret.ReturnDate,
                    partyName,
                    partyGstin,
                    pos,
                    r.Rate,
                    r.Taxable,
                    r.Cgst,
                    r.Sgst,
                    r.Igst,
                    ret.GrandTotal,
                    r.Hsn));
            }
        }

        var invoiceNumbers = sales.Select(s => s.InvoiceNumber).Where(n => !string.IsNullOrWhiteSpace(n)).OrderBy(n => n).ToList();
        var payload = new Gstr1Payload
        {
            gstin = gstin,
            fp = fp,
            gt = 0,
            cur_gt = 0,
            b2b = b2bByCtin.Values.ToList(),
            b2cs = b2csAgg.Values.Select(RoundB2cs).ToList(),
            cdnr = cdnrByCtin.Values.ToList(),
            hsn = new Gstr1Hsn
            {
                data = hsnAgg.Select((kv, i) => new Gstr1HsnRow
                {
                    num = i + 1,
                    hsn_sc = kv.Key.Hsn,
                    desc = kv.Key.Hsn == "UNMAPPED" ? "HSN not set on medicine" : kv.Key.Hsn,
                    uqc = "NOS",
                    qty = Round2(kv.Value.Qty),
                    rt = kv.Key.Rate,
                    txval = Round2(kv.Value.Taxable),
                    iamt = Round2(kv.Value.Igst),
                    camt = Round2(kv.Value.Cgst),
                    samt = Round2(kv.Value.Sgst),
                    csamt = 0
                }).ToList()
            },
            doc_issue = new Gstr1DocIssue
            {
                doc_det =
                [
                    new Gstr1DocDet
                    {
                        doc_num = 1,
                        docs =
                        [
                            new Gstr1DocRange
                            {
                                num = 1,
                                from = invoiceNumbers.FirstOrDefault() ?? "",
                                to = invoiceNumbers.LastOrDefault() ?? "",
                                totnum = sales.Count,
                                cancel = 0,
                                net_issue = sales.Count
                            }
                        ]
                    }
                ]
            }
        };

        return new GstReturnExportDto
        {
            Kind = GstReturnKind.Gstr1,
            Title = "GSTR-1 (outward supplies)",
            Gstin = gstin,
            FilingPeriod = fp,
            CompanyName = company?.CompanyName ?? "",
            FromDate = from.Date,
            ToDate = to.Date,
            Disclaimer =
                "Built from POS sales/returns. Review with your CA before GSTN upload. Not a substitute for the official GSTR-1 utility if e-invoice or extra tables apply.",
            PreviewRows = preview.OrderBy(r => r.InvoiceDate).ThenBy(r => r.InvoiceNumber).ToList(),
            JsonPayload = JsonSerializer.Serialize(payload, JsonOptions),
            ExcelSheets = BuildGstr1Sheets(company?.CompanyName, gstin, fp, from, to, preview, payload)
        };
    }

    public static async Task<GstReturnExportDto> BuildGstr2BAsync(
        IUnitOfWork uow,
        ISettingsService settings,
        IQueryable<Purchase> purchasesQuery,
        DateTime from,
        DateTime to,
        int? branchId,
        CancellationToken ct)
    {
        var (start, end) = (from.Date, to.Date.AddDays(1));
        var company = await settings.GetCompanyProfileAsync(ct);
        var gstin = NormalizeGstin(company?.GstNumber);
        var posDefault = StateCodeFromGstin(gstin) ?? "07";
        var fp = to.ToString("MMyyyy", CultureInfo.InvariantCulture);

        var purchases = await purchasesQuery
            .Where(p => p.InvoiceDate >= start && p.InvoiceDate < end)
            .Include(p => p.Supplier)
            .Include(p => p.Items).ThenInclude(i => i.Medicine)
            .OrderBy(p => p.InvoiceDate)
            .ToListAsync(ct);

        var prQuery = uow.Repository<PurchaseReturn>().Query()
            .Where(r => r.Status == PurchaseReturnStatus.Completed && r.ReturnDate >= start && r.ReturnDate < end);
        if (branchId.HasValue) prQuery = prQuery.Where(r => r.BranchId == branchId);

        var purchaseReturns = await prQuery
            .Include(r => r.Supplier)
            .Include(r => r.Items).ThenInclude(i => i.Medicine)
            .OrderBy(r => r.ReturnDate)
            .ToListAsync(ct);

        var preview = new List<GstReturnPreviewRowDto>();
        var b2b = new List<Gstr2bInvoice>();
        var unreg = new List<Gstr2bInvoice>();
        var cdn = new List<Gstr2bInvoice>();
        var itc = new Dictionary<decimal, (decimal Tx, decimal C, decimal S, decimal I)>();
        var hsnAgg = new Dictionary<(string Hsn, decimal Rate), HsnAgg>();

        foreach (var p in purchases)
        {
            var supplierGstin = NormalizeGstin(p.Supplier?.GstNumber);
            var isB2b = supplierGstin.Length == 15;
            var pos = isB2b ? StateCodeFromGstin(supplierGstin) ?? posDefault : posDefault;
            var interstate = p.IgstAmount > 0;
            var party = p.Supplier?.Name ?? $"Supplier #{p.SupplierId}";
            var invNo = string.IsNullOrWhiteSpace(p.SupplierInvoiceNumber) ? p.InvoiceNumber : p.SupplierInvoiceNumber;
            var rateRows = CollapseLines(p.Items.Select(i =>
                ToLine(i.GstPercent, i.TaxableAmount, i.TaxAmount, i.Medicine?.HsnCode, interstate, i.Quantity)));

            var dto = new Gstr2bInvoice
            {
                ctin = isB2b ? supplierGstin : null,
                inum = invNo,
                idt = p.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                val = Round2(p.GrandTotal),
                pos = pos,
                itms = rateRows.Select(r => new Gstr2bItem
                {
                    rt = r.Rate,
                    txval = r.Taxable,
                    iamt = r.Igst,
                    camt = r.Cgst,
                    samt = r.Sgst
                }).ToList()
            };

            if (isB2b) b2b.Add(dto);
            else unreg.Add(dto);

            foreach (var r in rateRows)
            {
                preview.Add(new GstReturnPreviewRowDto(
                    isB2b ? "B2B" : "Unregistered",
                    invNo,
                    p.InvoiceDate,
                    party,
                    string.IsNullOrEmpty(supplierGstin) ? null : supplierGstin,
                    pos,
                    r.Rate,
                    r.Taxable,
                    r.Cgst,
                    r.Sgst,
                    r.Igst,
                    p.GrandTotal,
                    r.Hsn));
                AddHsn(hsnAgg, r);
                AddItc(itc, r);
            }
        }

        foreach (var r in purchaseReturns)
        {
            var supplierGstin = NormalizeGstin(r.Supplier?.GstNumber);
            var pos = StateCodeFromGstin(supplierGstin) ?? posDefault;
            var interstate = r.CgstAmount == 0 && r.SgstAmount == 0 && r.TaxableAmount > 0 && r.GrandTotal > r.TaxableAmount;
            var party = r.Supplier?.Name ?? $"Supplier #{r.SupplierId}";
            var invNo = string.IsNullOrWhiteSpace(r.SupplierReturnReceiptNumber) ? r.ReturnNumber : r.SupplierReturnReceiptNumber;
            var rateRows = CollapseLines(r.Items.Select(i =>
                ToLine(i.GstPercent, i.TaxableAmount, i.TaxAmount, i.Medicine?.HsnCode, interstate, i.ReturnedQuantity)));

            cdn.Add(new Gstr2bInvoice
            {
                ctin = supplierGstin.Length == 15 ? supplierGstin : null,
                inum = invNo,
                idt = r.ReturnDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                val = Round2(r.GrandTotal),
                pos = pos,
                ntty = "C",
                itms = rateRows.Select(x => new Gstr2bItem
                {
                    rt = x.Rate,
                    txval = x.Taxable,
                    iamt = x.Igst,
                    camt = x.Cgst,
                    samt = x.Sgst
                }).ToList()
            });

            foreach (var x in rateRows)
            {
                preview.Add(new GstReturnPreviewRowDto(
                    "CDN",
                    invNo,
                    r.ReturnDate,
                    party,
                    string.IsNullOrEmpty(supplierGstin) ? null : supplierGstin,
                    pos,
                    x.Rate,
                    x.Taxable,
                    x.Cgst,
                    x.Sgst,
                    x.Igst,
                    r.GrandTotal,
                    x.Hsn));
            }
        }

        var payload = new Gstr2bPayload
        {
            gstin = gstin,
            fp = fp,
            style = "GSTR-2B-worksheet",
            note = "GSTR-2B is generated by GSTN. This file is a books-side ITC worksheet to match against the portal download.",
            b2b = b2b,
            unregistered = unreg,
            cdn = cdn,
            itcByRate = itc.OrderBy(k => k.Key).Select(k => new Gstr2bItcRate
            {
                rt = k.Key,
                txval = Round2(k.Value.Tx),
                camt = Round2(k.Value.C),
                samt = Round2(k.Value.S),
                iamt = Round2(k.Value.I)
            }).ToList(),
            hsn = hsnAgg.Select(kv => new Gstr1HsnRow
            {
                hsn_sc = kv.Key.Hsn,
                rt = kv.Key.Rate,
                qty = Round2(kv.Value.Qty),
                txval = Round2(kv.Value.Taxable),
                iamt = Round2(kv.Value.Igst),
                camt = Round2(kv.Value.Cgst),
                samt = Round2(kv.Value.Sgst)
            }).ToList()
        };

        return new GstReturnExportDto
        {
            Kind = GstReturnKind.Gstr2B,
            Title = "GSTR-2B (inward supplies / ITC worksheet)",
            Gstin = gstin,
            FilingPeriod = fp,
            CompanyName = company?.CompanyName ?? "",
            FromDate = from.Date,
            ToDate = to.Date,
            Disclaimer =
                "Worksheet from POS purchases/returns to reconcile with GSTR-2B on the GST portal. ITC eligibility (blocked credit, RCM) is not auto-classified.",
            PreviewRows = preview.OrderBy(r => r.InvoiceDate).ThenBy(r => r.InvoiceNumber).ToList(),
            JsonPayload = JsonSerializer.Serialize(payload, JsonOptions),
            ExcelSheets = BuildGstr2bSheets(company?.CompanyName, gstin, fp, from, to, preview, payload)
        };
    }

    private static GstLine ToLine(decimal gstPercent, decimal taxable, decimal tax, string? hsn, bool interstate, decimal qty)
    {
        var rate = Round2(gstPercent);
        decimal igst = 0, cgst = 0, sgst = 0;
        var t = Round2(tax);
        var tx = Round2(taxable);
        if (interstate)
            igst = t;
        else
        {
            cgst = Round2(t / 2m);
            sgst = Round2(t - cgst);
        }

        var code = string.IsNullOrWhiteSpace(hsn) ? "UNMAPPED" : hsn.Trim();
        return new GstLine(rate, tx, cgst, sgst, igst, code, qty);
    }

    private static List<GstLine> CollapseLines(IEnumerable<GstLine> lines) =>
        lines.GroupBy(l => (l.Rate, l.Hsn))
            .Select(g => new GstLine(
                g.Key.Rate,
                Round2(g.Sum(x => x.Taxable)),
                Round2(g.Sum(x => x.Cgst)),
                Round2(g.Sum(x => x.Sgst)),
                Round2(g.Sum(x => x.Igst)),
                g.Key.Hsn,
                g.Sum(x => x.Qty)))
            .ToList();

    private static List<GstLine> CollapseByRate(IEnumerable<GstLine> lines) =>
        lines.GroupBy(l => l.Rate)
            .Select(g => new GstLine(
                g.Key,
                Round2(g.Sum(x => x.Taxable)),
                Round2(g.Sum(x => x.Cgst)),
                Round2(g.Sum(x => x.Sgst)),
                Round2(g.Sum(x => x.Igst)),
                g.First().Hsn,
                g.Sum(x => x.Qty)))
            .ToList();

    private static void AddHsn(Dictionary<(string Hsn, decimal Rate), HsnAgg> map, GstLine line)
    {
        var key = (line.Hsn, line.Rate);
        if (!map.TryGetValue(key, out var agg))
        {
            agg = new HsnAgg();
            map[key] = agg;
        }
        agg.Taxable += line.Taxable;
        agg.Cgst += line.Cgst;
        agg.Sgst += line.Sgst;
        agg.Igst += line.Igst;
        agg.Qty += line.Qty;
    }

    private static void AddItc(Dictionary<decimal, (decimal Tx, decimal C, decimal S, decimal I)> map, GstLine line)
    {
        map.TryGetValue(line.Rate, out var cur);
        map[line.Rate] = (cur.Tx + line.Taxable, cur.C + line.Cgst, cur.S + line.Sgst, cur.I + line.Igst);
    }

    private static Gstr1B2csRow RoundB2cs(Gstr1B2csRow r)
    {
        r.txval = Round2(r.txval);
        r.iamt = Round2(r.iamt);
        r.camt = Round2(r.camt);
        r.samt = Round2(r.samt);
        return r;
    }

    private static List<GstReturnExcelSheetDto> BuildGstr1Sheets(
        string? company, string gstin, string fp, DateTime from, DateTime to,
        List<GstReturnPreviewRowDto> preview, Gstr1Payload payload)
    {
        return
        [
            Cover("GSTR-1", company, gstin, fp, from, to,
                "Outward supplies from sales. JSON follows GSTR-1-style tables (b2b, b2cs, cdnr, hsn, doc_issue)."),
            PreviewSheet("All lines", preview),
            PreviewSheet("B2B", preview.Where(r => r.Section == "B2B").ToList()),
            PreviewSheet("B2CS", preview.Where(r => r.Section == "B2CS").ToList()),
            PreviewSheet("CDNR", preview.Where(r => r.Section == "CDNR").ToList()),
            new GstReturnExcelSheetDto
            {
                Name = "HSN",
                Headers = ["HSN", "UQC", "Qty", "Rate", "Taxable", "IGST", "CGST", "SGST"],
                Rows = payload.hsn.data.Select(h => (IReadOnlyList<string>)
                [
                    h.hsn_sc ?? "", "NOS", Dec(h.qty), Dec(h.rt), Dec(h.txval), Dec(h.iamt), Dec(h.camt), Dec(h.samt)
                ]).ToList()
            }
        ];
    }

    private static List<GstReturnExcelSheetDto> BuildGstr2bSheets(
        string? company, string gstin, string fp, DateTime from, DateTime to,
        List<GstReturnPreviewRowDto> preview, Gstr2bPayload payload)
    {
        return
        [
            Cover("GSTR-2B worksheet", company, gstin, fp, from, to,
                "Inward supplies from purchases. Match invoice-wise ITC with the GSTR-2B PDF/JSON from GSTN."),
            PreviewSheet("All lines", preview),
            PreviewSheet("B2B", preview.Where(r => r.Section == "B2B").ToList()),
            PreviewSheet("Unregistered", preview.Where(r => r.Section == "Unregistered").ToList()),
            PreviewSheet("CDN", preview.Where(r => r.Section == "CDN").ToList()),
            new GstReturnExcelSheetDto
            {
                Name = "ITC by rate",
                Headers = ["GST %", "Taxable", "CGST", "SGST", "IGST", "Total tax"],
                Rows = payload.itcByRate.Select(r => (IReadOnlyList<string>)
                [
                    Dec(r.rt), Dec(r.txval), Dec(r.camt), Dec(r.samt), Dec(r.iamt),
                    Dec(r.camt + r.samt + r.iamt)
                ]).ToList()
            }
        ];
    }

    private static GstReturnExcelSheetDto Cover(
        string title, string? company, string gstin, string fp, DateTime from, DateTime to, string note) =>
        new()
        {
            Name = "Cover",
            Headers = ["Field", "Value"],
            Rows =
            [
                ["Report", title],
                ["Company", company ?? ""],
                ["GSTIN", gstin],
                ["Filing period (MMYYYY)", fp],
                ["From", from.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)],
                ["To", to.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture)],
                ["Note", note]
            ]
        };

    private static GstReturnExcelSheetDto PreviewSheet(string name, List<GstReturnPreviewRowDto> rows) =>
        new()
        {
            Name = name,
            Headers =
            [
                "Section", "Invoice", "Date", "Party", "GSTIN", "POS", "Rate", "Taxable",
                "CGST", "SGST", "IGST", "Invoice value", "HSN"
            ],
            Rows = rows.Select(r => (IReadOnlyList<string>)
            [
                r.Section, r.InvoiceNumber, r.InvoiceDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture),
                r.PartyName, r.Gstin ?? "", r.PlaceOfSupply, Dec(r.GstRate), Dec(r.TaxableAmount),
                Dec(r.CgstAmount), Dec(r.SgstAmount), Dec(r.IgstAmount), Dec(r.InvoiceValue), r.HsnCode ?? ""
            ]).ToList()
        };

    private static string Dec(decimal n) => n.ToString("0.##", CultureInfo.InvariantCulture);

    private static decimal Round2(decimal n) => Math.Round(n, 2, MidpointRounding.AwayFromZero);

    private static string NormalizeGstin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var s = new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return s;
    }

    private static string? StateCodeFromGstin(string gstin)
    {
        if (gstin.Length >= 2 && char.IsDigit(gstin[0]) && char.IsDigit(gstin[1]))
            return gstin[..2];
        return null;
    }

    private sealed record GstLine(decimal Rate, decimal Taxable, decimal Cgst, decimal Sgst, decimal Igst, string Hsn, decimal Qty);

    private sealed class HsnAgg
    {
        public decimal Qty;
        public decimal Taxable;
        public decimal Cgst;
        public decimal Sgst;
        public decimal Igst;
    }

    private sealed class Gstr1Payload
    {
        public string gstin { get; set; } = "";
        public string fp { get; set; } = "";
        public decimal gt { get; set; }
        public decimal cur_gt { get; set; }
        public List<Gstr1B2bParty> b2b { get; set; } = [];
        public List<Gstr1B2csRow> b2cs { get; set; } = [];
        public List<Gstr1CdnrParty> cdnr { get; set; } = [];
        public Gstr1Hsn hsn { get; set; } = new();
        public Gstr1DocIssue doc_issue { get; set; } = new();
    }

    private sealed class Gstr1B2bParty
    {
        public string ctin { get; set; } = "";
        public List<Gstr1Invoice> inv { get; set; } = [];
    }

    private sealed class Gstr1Invoice
    {
        public string inum { get; set; } = "";
        public string idt { get; set; } = "";
        public decimal val { get; set; }
        public string pos { get; set; } = "";
        public string rchrg { get; set; } = "N";
        public string inv_typ { get; set; } = "R";
        public List<Gstr1Item> itms { get; set; } = [];
    }

    private sealed class Gstr1Item
    {
        public int num { get; set; }
        public Gstr1ItemDet itm_det { get; set; } = new();
    }

    private sealed class Gstr1ItemDet
    {
        public decimal rt { get; set; }
        public decimal txval { get; set; }
        public decimal iamt { get; set; }
        public decimal camt { get; set; }
        public decimal samt { get; set; }
        public decimal csamt { get; set; }
    }

    private sealed class Gstr1B2csRow
    {
        public string sply_ty { get; set; } = "INTRA";
        public decimal rt { get; set; }
        public string typ { get; set; } = "OE";
        public string pos { get; set; } = "";
        public decimal txval { get; set; }
        public decimal iamt { get; set; }
        public decimal camt { get; set; }
        public decimal samt { get; set; }
        public decimal csamt { get; set; }
    }

    private sealed class Gstr1CdnrParty
    {
        public string ctin { get; set; } = "";
        public List<Gstr1CreditNote> nt { get; set; } = [];
    }

    private sealed class Gstr1CreditNote
    {
        public string ntty { get; set; } = "C";
        public string nt_num { get; set; } = "";
        public string nt_dt { get; set; } = "";
        public string inum { get; set; } = "";
        public string idt { get; set; } = "";
        public decimal val { get; set; }
        public string pos { get; set; } = "";
        public string rchrg { get; set; } = "N";
        public string inv_typ { get; set; } = "R";
        public List<Gstr1Item> itms { get; set; } = [];
    }

    private sealed class Gstr1Hsn
    {
        public List<Gstr1HsnRow> data { get; set; } = [];
    }

    private sealed class Gstr1HsnRow
    {
        public int num { get; set; }
        public string? hsn_sc { get; set; }
        public string? desc { get; set; }
        public string? uqc { get; set; }
        public decimal qty { get; set; }
        public decimal rt { get; set; }
        public decimal txval { get; set; }
        public decimal iamt { get; set; }
        public decimal camt { get; set; }
        public decimal samt { get; set; }
        public decimal csamt { get; set; }
    }

    private sealed class Gstr1DocIssue
    {
        public List<Gstr1DocDet> doc_det { get; set; } = [];
    }

    private sealed class Gstr1DocDet
    {
        public int doc_num { get; set; }
        public List<Gstr1DocRange> docs { get; set; } = [];
    }

    private sealed class Gstr1DocRange
    {
        public int num { get; set; }
        public string from { get; set; } = "";
        public string to { get; set; } = "";
        public int totnum { get; set; }
        public int cancel { get; set; }
        public int net_issue { get; set; }
    }

    private sealed class Gstr2bPayload
    {
        public string gstin { get; set; } = "";
        public string fp { get; set; } = "";
        public string style { get; set; } = "";
        public string note { get; set; } = "";
        public List<Gstr2bInvoice> b2b { get; set; } = [];
        public List<Gstr2bInvoice> unregistered { get; set; } = [];
        public List<Gstr2bInvoice> cdn { get; set; } = [];
        public List<Gstr2bItcRate> itcByRate { get; set; } = [];
        public List<Gstr1HsnRow> hsn { get; set; } = [];
    }

    private sealed class Gstr2bInvoice
    {
        public string? ctin { get; set; }
        public string inum { get; set; } = "";
        public string idt { get; set; } = "";
        public decimal val { get; set; }
        public string pos { get; set; } = "";
        public string? ntty { get; set; }
        public List<Gstr2bItem> itms { get; set; } = [];
    }

    private sealed class Gstr2bItem
    {
        public decimal rt { get; set; }
        public decimal txval { get; set; }
        public decimal iamt { get; set; }
        public decimal camt { get; set; }
        public decimal samt { get; set; }
    }

    private sealed class Gstr2bItcRate
    {
        public decimal rt { get; set; }
        public decimal txval { get; set; }
        public decimal camt { get; set; }
        public decimal samt { get; set; }
        public decimal iamt { get; set; }
    }
}

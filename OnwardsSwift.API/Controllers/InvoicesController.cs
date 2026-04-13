using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;

namespace OnwardsSwift.API.Controllers
{
    public class InvoicesController : AppController
    {
        private readonly IInvoiceDiscountService _inv;
        private readonly IClientService          _clients;

        public InvoicesController(IInvoiceDiscountService inv, IClientService clients)
        { _inv = inv; _clients = clients; }

        public async Task<IActionResult> Index(int page = 1)
        {
            var result = await _inv.GetAllAsync(new FacilityFilter { Page = page, PageSize = 20 });
            return View(result);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateClients();
            return View(new CreateInvoiceDiscountRequest
            {
                InvoiceDate   = DateTime.Today,
                InvoiceDueDate= DateTime.Today.AddDays(60),
                AdvancePercentage = 80
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateInvoiceDiscountRequest model)
        {
            if (!ModelState.IsValid) { await PopulateClients(); return View(model); }
            var inv = await _inv.CreateAsync(model, CurrentUserEmail,5);
            Success($"Invoice discounting {inv.ReferenceNo} submitted.");
            return RedirectToAction(nameof(Details), new { id = inv.FacilityId });
        }

        public async Task<IActionResult> Details(int id)
        {
            var inv = await _inv.GetByIdAsync(id);
            if (inv == null) return NotFound();
            return View(inv);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(Guid id, string? notes)
        {
            Success("Approved."); return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Disburse(Guid id)
        {
            Success("Disbursed."); return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(Guid id, string reason)
        {
            Success("Rejected."); return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordPayment(Guid id, decimal paymentAmount, DateTime paymentDate)
        {
            await _inv.RecordDebtorPaymentAsync(new RecordDebtorPaymentRequest
            { FacilityId = id, PaymentAmount = paymentAmount, PaymentDate = paymentDate }, CurrentUserEmail);
            Success("Debtor payment recorded. Facility settled.");
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PopulateClients()
        {
            var clients = await _clients.GetAllAsync(1, 200, null);
            ViewBag.Clients = new SelectList(clients.Items, "Id", "CompanyName");
        }
    }
}

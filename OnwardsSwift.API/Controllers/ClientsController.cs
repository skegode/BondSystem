using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;

namespace OnwardsSwift.API.Controllers
{
    public class ClientsController : AppController
    {
        private readonly IClientService _clients;
        public ClientsController(IClientService clients) => _clients = clients;

        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var result = await _clients.GetAllAsync(page, 20, search);
            ViewBag.Search = search;
            return View(result);
        }

        [HttpGet]
        public IActionResult Create() => View(new CreateClientRequest());

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateClientRequest model)
        {
            if (!ModelState.IsValid) return View(model);
            var client = await _clients.CreateAsync(model, CurrentUserEmail);
            Success($"Client '{client.CompanyName}' created.");
            return RedirectToAction(nameof(Details), new { id = client.Id });
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = await _clients.GetByIdAsync(id);
            if (client == null) return NotFound();
            var facilities = await _clients.GetClientFacilitiesAsync(id);
            ViewBag.Facilities = facilities;
            return View(client);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var client = await _clients.GetByIdAsync(id);
            if (client == null) return NotFound();
            var model = new CreateClientRequest
            {
                CompanyName     = client.CompanyName,
                KraPin          = client.KraPin,
                ContactPerson   = client.ContactPerson,
                Email           = client.Email,
                Phone           = client.Phone,
                PhysicalAddress = client.PhysicalAddress,
                CreditLimit     = client.CreditLimit
            };
            ViewBag.ClientId = id;
            ViewBag.ClientName = client.CompanyName;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateClientRequest model)
        {
            if (!ModelState.IsValid) { ViewBag.ClientId = id; return View(model); }
            await _clients.UpdateAsync(id, model, CurrentUserEmail);
            Success("Client updated.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, int status, string? reason)
        {
            await _clients.UpdateStatusAsync(id, status, reason, CurrentUserEmail);
            Success("Client status updated.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyCrb(int id)
        {
            await _clients.VerifyCrbAsync(id, CurrentUserEmail);
            Success("CRB verification recorded.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCreditLimit(int id, decimal limit)
        {
            await _clients.UpdateCreditLimitAsync(id, limit, CurrentUserEmail);
            Success("Credit limit updated.");
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}

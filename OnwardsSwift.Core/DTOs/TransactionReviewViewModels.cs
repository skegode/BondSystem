using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class TransactionReviewListItem
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string SubmittedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Summary { get; set; } = string.Empty;
        public bool IsStep3Completed { get; set; } = false;

        public bool IsNew => CreatedAt >= DateTime.UtcNow.AddHours(-24);
    }

    // ── Portal Profile index (two-section view) ─────────────────────────────────
    public class PortalProfileIndexViewModel
    {
        public List<TransactionReviewListItem> ChequeSubmissions { get; set; } = new();
        public List<TransactionReviewListItem> BondSubmissions   { get; set; } = new();
    }

    // ── Status workflow + history (shared by cheque/bond review screens) ───────────
    public class RequestStatusEntry
    {
        public string  Status     { get; set; } = string.Empty;
        public string? StatusNote { get; set; }
        public string? ChangedBy  { get; set; }
        public DateTime ChangedAtUtc { get; set; }
    }

    // ── Step 2: Cheque Encashment ────────────────────────────────────────────────
    public class ChequeEncashmentReviewViewModel
    {
        public int    Id            { get; set; }
        public int?   ClientId      { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string IdNumber      { get; set; } = string.Empty;
        public string PostalAddress { get; set; } = string.Empty;
        public string Phone         { get; set; } = string.Empty;
        public string Purpose       { get; set; } = string.Empty;
        public bool   TermsAccepted { get; set; }
        public string CreatedBy     { get; set; } = string.Empty;
        public DateTime CreatedAt   { get; set; }
        public string  Category        { get; set; } = string.Empty;
        public string  PaymentMethod   { get; set; } = string.Empty;
        public string? DisburseBank    { get; set; }
        public string? DisburseAccount { get; set; }
        public List<ChequeItem>    Cheques     { get; set; } = new();
        public List<AttachmentItem> Attachments { get; set; } = new();
        public int  CurrentStatus { get; set; }
        public List<RequestStatusEntry> StatusHistory { get; set; } = new();
    }

    // ── Step 3: Official Use ─────────────────────────────────────────────────────
    public class OfficialUseReviewViewModel
    {
        public int      Id                       { get; set; }
        public int?     RequestId                { get; set; }
        public string?  CheckedBy                { get; set; }
        public string?  CheckedSignature         { get; set; }
        public string?  CheckedDate              { get; set; }
        public string?  ConfirmedWith            { get; set; }
        public string?  Designation              { get; set; }
        public string?  BuildingStreet           { get; set; }
        public string?  DrawerStatus             { get; set; }
        public string?  ReasonForPayment         { get; set; }
        public string?  AccountConfirmedBy       { get; set; }
        public string?  AccountStatus            { get; set; }
        public string?  HeadOfTradeFinance       { get; set; }
        public string?  HeadOfTradeSignature     { get; set; }
        public string?  HeadOfTradeDate          { get; set; }
        public string?  InChargeFinance          { get; set; }
        public string?  InChargeFinanceSignature { get; set; }
        public string?  InChargeFinanceDate      { get; set; }
        public string?  CEO                      { get; set; }
        public string?  CEOSignature             { get; set; }
        public string?  CEODate                  { get; set; }
        public string?  PaidByName               { get; set; }
        public string?  PaidBySignature          { get; set; }
        public DateTime CreatedAt                { get; set; }
        public string?  CreatedBy                { get; set; }
    }

    // ── Full Onboarding Wizard profile (all 3 steps) ─────────────────────────────
    public class OnboardingWizardProfileViewModel
    {
        // Step 1 — Client (populated if ClientId was linked on the cheque request)
        public int?    ClientId    { get; set; }
        public string? ClientName  { get; set; }
        public string? ClientEmail { get; set; }
        public string? ClientPhone { get; set; }
        public string? ClientType  { get; set; }
        public string? ClientKraPin { get; set; }

        // Step 2 — Cheque Encashment
        public ChequeEncashmentReviewViewModel ChequeData { get; set; } = new();

        // Step 3 — Official Use (null = not yet submitted)
        public OfficialUseReviewViewModel? OfficialUse { get; set; }
    }

    // ── Bond Application review ──────────────────────────────────────────────────
    public class BondApplicationReviewViewModel
    {
        public int    Id         { get; set; }
        public BondApplicationViewModel Data { get; set; } = new();
        public string CreatedBy  { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<string> Attachments { get; set; } = new();
        public int  CurrentStatus { get; set; }
        public List<RequestStatusEntry> StatusHistory { get; set; } = new();
    }

    public class AttachmentItem
    {
        public string FileName    { get; set; } = string.Empty;
        public string FilePath    { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }
}

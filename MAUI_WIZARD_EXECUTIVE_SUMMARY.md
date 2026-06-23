# 🚀 MAUI Onboarding Wizard - Executive Summary

## Project Overview

**Objective**: Replicate both guided workflows in the ASP.NET Core portal to a cross-platform .NET MAUI application (iOS, Android, macOS, Windows):
- Client Onboarding Wizard
- Bank Guarantee / Bond Request Wizard

**Status**: Pre-development (Specification Phase)  
**Timeline**: 20-21 weeks (June-October 2026)  
**Team Size**: 2-3 people

---

## 📊 At-a-Glance Breakdown

### What's Being Built

| Aspect | Details |
|--------|---------|
| **Scope** | 3-step wizard + transaction history + offline support |
| **Platforms** | iOS, Android, macOS, Windows |
| **Framework** | .NET MAUI 8.0 (C#/XAML) |
| **Data Layer** | SQL Server backend + SQLite offline |
| **Authentication** | Cookie-based (8-hour sliding) |
| **Theme** | Navy blue (#002D72) Bootstrap 5.3 design |

### Core Features

```
✅ STEP 1: Client Registration (Individuals/SME/Corporate/Government)
   - Client profile data capture
   - KYC document uploads (ID, passport, certificates)
   - IPRS verification (Kenya national ID)
   - Optional existing client search

✅ STEP 2: Cheque Encashment Request
   - Applicant information (name, ID, address, phone)
   - Dynamic cheque entry (add/remove multiple cheques)
   - Per-cheque details: number, amount, date, drawer, bank, branch, payee
   - Supporting document uploads (JPG, PNG, PDF, DOCX)
   - Declarant information & date
   - Terms & conditions acceptance

✅ STEP 3: Official Use (Multi-Level Approvals)
   - Drawer verification (confirmed with, designation, address, status, reason)
   - Account confirmation (account holder, status)
   - 5 sequential signatories with digital signature capture:
     1. Checked By
     2. Head of Trade Finance
     3. In Charge Finance
     4. CEO
     5. Paid By

✅ Transaction History & Details
   - View past submissions
   - Drill into full transaction details
   - Edit incomplete requests (jump to Step 3)
   - Export/share functionality

✅ Bond Request Wizard
   - Two-step bank guarantee / bond application workflow
   - Applicant + beneficiary details capture
   - Bond type selection and guarantee dates
   - Counter guarantee / indemnity wording + signatory capture
   - File attachments and digital signature files

✅ Offline Mode
   - Draft requests saved to local SQLite
   - Auto-sync when network available
```

---

## 💾 Database Schema (Simplified)

```
Clients (Step 1)
├── Id, ClientType, CompanyName, KraPin
├── ContactPerson, ContactEmail, Phone
├── IdNumber, Gender, PhysicalAddress
└── IprsVerified (boolean)

ChequeEncashmentRequests (Step 2 main)
├── Id, ClientId (optional FK)
├── ApplicantName, IdNumber, Phone, PostalAddress
├── Purpose, DeclarantName, DeclarantRole, DeclarantDate
├── TermsAccepted
└── CreatedAt, UpdatedAt

ChequeEncashmentCheques (Step 2 items - repeating)
├── RequestId (FK → parent request)
├── ChequeNumber, Amount, Date
├── Drawer, Bank, Branch, Payee
└── (One row per cheque in request)

ChequeEncashmentAttachments (Step 2 files)
├── RequestId (FK)
├── FilePath, FileName, FileSize
└── (One row per uploaded file)

OfficialUseRecords (Step 3)
├── RequestId (FK → cheque request)
├── CheckedBy, CheckedSignature, CheckedDate
├── HeadOfTradeFinance, HeadOfTradeSignature, HeadOfTradeDate
├── InChargeFinance, InChargeFinanceSignature, InChargeFinanceDate
├── CEO, CEOSignature, CEODate
├── PaidByName, PaidBySignature
├── DrawerStatus, AccountStatus (enums)
└── (5 approval records in single row)
```

---

## 🎯 Key Technical Components

### Services Layer
```
ApiService
  ├── POST /api/clients (Step 1)
  ├── GET /api/iprs/{id} (Verification)
  ├── POST /api/cheques/request (Step 2)
  ├── POST /api/cheques/items
  ├── POST /api/cheques/attachments
  ├── POST /api/officialuse (Step 3)
  └── GET /api/transactions (History)

WizardService
  ├── ValidateStep(step, data)
  ├── SaveStep(step, data)
  ├── GetDraft(userId)
  └── SyncOfflineDrafts()

StorageService
  ├── UploadKycFile(file, type)
  ├── UploadAttachment(file)
  ├── SaveSignature(imageData, role)
  └── ClearTemporaryFiles()

IprsService
  ├── VerifyNationalId(id)
  └── GetVerificationStatus()
```

### UI Components (XAML)
```
WizardPage
├── Step1Page (Client Registration)
│   ├── ClientTypeSelector
│   ├── FormFields (name, email, phone, address)
│   ├── FileUploadControl (KYC documents)
│   ├── IprsVerificationBadge
│   └── NavigationButtons

├── Step2Page (Cheque Encashment)
│   ├── ApplicantInfoSection
│   ├── PurposeInput
│   ├── ChequeListView (repeating)
│   │   └── ChequeDetailCard (per cheque)
│   ├── FileUploadControl (attachments)
│   ├── DeclarantInfoSection
│   ├── TermsScrollView
│   └── NavigationButtons

├── Step3Page (Approvals)
│   ├── DrawerVerificationSection
│   ├── AccountConfirmationSection
│   ├── SignatoryStack (x5)
│   │   ├── NameInput
│   │   ├── SignaturePadView (SkiaSharp-based)
│   │   └── DatePicker
│   └── NavigationButtons

HistoryPage
├── TransactionListView
│   └── TransactionCard (per request)
└── DetailPage (drill-in)
```

---

## 🔄 Navigation Flow

```
┌──────────────┐
│  Landing     │
│  Page        │
└──────┬───────┘
       │ [Start New Request]
       ↓
┌──────────────────────────────────┐
│ STEP 1: CLIENT REGISTRATION      │ (2-3 mins)
│ - Client type selection           │
│ - Profile data entry              │
│ - KYC document upload             │
│ - IPRS verification               │
└──────┬───────────────────────────┘
       │ [Next]
       ↓
┌──────────────────────────────────┐
│ STEP 2: CHEQUE ENCASHMENT        │ (5-7 mins)
│ - Applicant info                  │
│ - Dynamic cheque entry            │
│ - File attachments                │
│ - Declarant info                  │
│ - T&C acceptance                  │
└──────┬───────────────────────────┘
       │ [Next]
       ↓
┌──────────────────────────────────┐
│ STEP 3: OFFICIAL USE             │ (8-10 mins)
│ - Drawer verification             │
│ - Account confirmation            │
│ - 5 levels of approval signatures │
└──────┬───────────────────────────┘
       │ [Complete Submission]
       ↓
┌──────────────────────────────────┐
│ SUCCESS SCREEN                   │
│ - Show Request ID                │
│ - Option to view transaction     │
└──────────────────────────────────┘

Total Time: ~15-20 minutes per complete submission
```

---

## 📋 Implementation Phases

### Phase 1: Foundation (Weeks 1-3)
- Project scaffolding & DI setup
- Navigation infrastructure
- HTTP client + retry policies
- SQLite configuration
- **Output**: Runnable app shell

### Phase 2: Step 1 (Weeks 3-6)
- Client model & data binding
- Form validation (MVVM + FluentValidation)
- File picker & KYC upload service
- IPRS integration
- **Output**: Complete Step 1 with verification

### Phase 3: Step 2 (Weeks 6-10)
- ChequeEncashmentRequest model
- Repeating cheque controls
- Dynamic add/remove cheques
- File attachment service
- **Output**: Fully functional Step 2

### Phase 4: Step 3 (Weeks 10-13)
- OfficialUseRecord model
- Signature pad (SkiaSharp)
- 5-level approval flow
- Signature capture & storage
- **Output**: Complete Step 3 with signatures

### Phase 5: History & Details (Weeks 13-14)
- Transaction list view
- Drill-in detail page
- Edit existing requests
- **Output**: View & manage past submissions

### Phase 6: Offline & Sync (Weeks 14-16)
- SQLite local database
- Draft management
- Auto-sync when online
- **Output**: Works offline + syncs

### Phase 7: Testing & Refinement (Weeks 16-19)
- Unit tests (ViewModels, services)
- Integration tests (API calls, DB)
- UI tests (all 3 steps)
- Platform-specific testing (iOS/Android/Windows)
- **Output**: 60%+ code coverage, bug fixes

### Phase 8: Deployment (Week 19-20)
- App signing & provisioning
- Store submissions (Apple/Google/Microsoft)
- Release documentation
- **Output**: Production apps ready

---

## ⏱️ Timeline Summary

```
Week 1  ├─ Project Setup & Architecture
Week 2  ├─ Navigation & DI Configuration
Week 3  ├─ Step 1 - Client Registration
Week 4  ├─ Step 1 - KYC Upload & IPRS
Week 5  ├─ Step 1 - Testing & Refinement
Week 6  ├─ Step 2 - Basic Form Layout
Week 7  ├─ Step 2 - Repeating Cheques
Week 8  ├─ Step 2 - File Uploads
Week 9  ├─ Step 2 - Declarant & T&C
Week 10 ├─ Step 3 - Drawer & Account Info
Week 11 ├─ Step 3 - Signature Pad & Approvals
Week 12 ├─ Step 3 - Multi-Level Signatures
Week 13 ├─ History Page & Details View
Week 14 ├─ Edit Workflow & Navigation
Week 15 ├─ Offline Mode & SQLite
Week 16 ├─ Sync & Conflict Resolution
Week 17 ├─ Unit & Integration Testing
Week 18 ├─ UI Testing & Bug Fixes
Week 19 ├─ Platform Testing & Optimization
Week 20 ├─ App Signing & Store Submission
       └─ 🎉 PRODUCTION RELEASE

Total: **20 weeks**
Buffer: +2-3 weeks for unforeseen issues
```

---

## 👥 Team Requirements

| Role | Effort | Duration |
|------|--------|----------|
| Senior MAUI Developer | 100% | 20 weeks |
| Backend API Developer (support) | 20% | 4-5 weeks |
| QA/Tester | 60% | Weeks 7-20 |

---

## 💰 Estimated Effort

- **Developer Hours**: 800-900 hours (20 weeks @ 40-45 hrs/week)
- **Testing Hours**: 200-250 hours
- **Infrastructure/DevOps**: 40-50 hours
- **Documentation**: 30-40 hours
- **Total**: ~1,100-1,200 hours

**Cost Estimate** (excluding team): $40,000-$55,000 USD (depending on rates & location)

---

## 📦 Deliverables

### Functional
- ✅ Complete 3-step wizard (all business logic)
- ✅ Transaction history & details view
- ✅ Offline draft support with sync
- ✅ iOS app (Apple App Store)
- ✅ Android app (Google Play)
- ✅ Windows app (Microsoft Store)
- ✅ macOS app (Mac App Store)

### Technical
- ✅ Source code (GitHub with CI/CD)
- ✅ Unit & integration tests (60%+ coverage)
- ✅ API documentation (endpoints, DTOs)
- ✅ Deployment guide (store submission steps)
- ✅ User manual (screenshots, workflows)
- ✅ Architecture documentation

### Quality Metrics
- ✅ 95%+ form validation accuracy
- ✅ < 2 seconds average screen load time
- ✅ < 100MB app bundle size
- ✅ Zero critical bugs in UAT
- ✅ Platform coverage: iOS 14+, Android 8+, Windows 10+

---

## 🎨 UI/UX Features

- **Navy theme** (#002D72) matching existing web app
- **Responsive layout** for phones, tablets, desktop
- **Touch-optimized** controls (larger buttons, spacing)
- **Signature pad** with draw/clear/accept
- **Progress indicator** (Step X of 3)
- **Real-time validation** with error messages
- **File picker** with preview thumbnails
- **IPRS verification badge** (visual confirmation)
- **Offline indicator** (show sync status)
- **Dark mode support** (optional enhancement)

---

## 🔐 Security Features

- ✅ HTTPS/TLS for all API calls
- ✅ Certificate pinning (optional)
- ✅ Cookie-based authentication (8-hour expiration)
- ✅ Local encryption (SQLite + sensitive data)
- ✅ SQL injection prevention (parameterized queries)
- ✅ Input validation (client + server)
- ✅ File upload restrictions (whitelist extensions)
- ✅ PII masking in UI

---

## 🚀 Go-Live Readiness

**Pre-Launch Checklist**
- [ ] All 3 steps tested end-to-end
- [ ] API integration verified
- [ ] Offline mode tested
- [ ] All platforms (iOS/Android/Windows) tested
- [ ] Performance profiling completed
- [ ] Security audit passed
- [ ] App store submissions approved
- [ ] User documentation complete
- [ ] Support team trained

**Launch Strategy**
1. Soft launch to internal users (1 week)
2. Limited public beta (1-2 weeks)
3. Full production release (after feedback incorporation)

---

## 📈 Success Metrics

| Metric | Target |
|--------|--------|
| App Store Rating | ≥ 4.5 stars |
| Crash Rate | < 0.1% |
| Avg Session Duration | > 15 mins |
| Form Completion Rate | > 85% |
| User Retention (7-day) | > 60% |
| App Size | < 100 MB |
| Startup Time | < 3 seconds |

---

## ⚠️ Key Risks & Mitigation

| Risk | Impact | Mitigation |
|------|--------|-----------|
| IPRS API delays | High | Use mock service initially, integrate later |
| File upload size | Medium | Implement compression + chunked upload |
| Signature compatibility | Medium | Support multiple formats (draw, upload, typed) |
| Platform differences | Medium | Early testing on all platforms (Week 2) |
| Backend API changes | High | Version API, maintain backward compatibility |
| Offline sync conflicts | Medium | Implement conflict resolution strategy |

---

## 🔗 Related Documentation

- [Full Specification](./MAUI_WIZARD_SPECIFICATION.md)
- [ASP.NET Core API Implementation](./OnwardsSwift.API/)
- [Database Schema](./AddChequeEncashmentTables.sql)
- [Existing Web Wizard Views](./OnwardsSwift.API/Views/Forms/)

---

## 📞 Next Steps

1. **Review This Document** → Stakeholder approval
2. **Detailed Design** → UX/UI mockups, API contracts
3. **Environment Setup** → .NET MAUI 8.0, Visual Studio 2022
4. **Team Kick-off** → Sprint planning, backlog refinement
5. **Development Sprint 1** → Project scaffolding (Week 1-3)

---

**Document Version**: 1.0  
**Last Updated**: June 8, 2026  
**Status**: Ready for Development


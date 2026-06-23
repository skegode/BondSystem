# .NET MAUI Onboarding Wizard App - Complete Specification

**Project**: OnwardsSwift Onboarding Wizard (Mobile/Desktop)  
**Platform**: .NET MAUI (iOS, Android, macOS, Windows)  
**Date**: 2026-06-08  

---

## 📋 EXECUTIVE SUMMARY

This document outlines a complete replication of the OnwardsSwift 3-step onboarding wizard from ASP.NET Core to a .NET MAUI cross-platform application. The wizard processes client registrations, cheque encashments, and official approvals—integrating with the existing backend API.

---

## 🎯 WIZARD OVERVIEW

This repository contains two distinct guided workflows:

1. **Client Onboarding Wizard** — a 3-step process for client onboarding, cheque encashment requests, and official approval capture.
2. **Bond Request Wizard** — a 2-step process for submitting bank guarantee / bond applications and completing the indemnity/counter-guarantee section.

### Client Onboarding Wizard

The onboarding wizard is a **3-step sequential form** that collects and processes:

```
┌─────────────────────────────────────────────────────────────────┐
│  STEP 1: CLIENT REGISTRATION                                     │
│  └─ Capture client profile (Individual/SME/Corporate)           │
│  └─ Verify with IPRS (Kenya national ID)                        │
│  └─ Upload KYC documents (ID, passport, certificate)            │
│  └─ Auto-create Client in database                              │
└─────────────────────────────────────────────────────────────────┐
                           ↓
┌─────────────────────────────────────────────────────────────────┐
│  STEP 2: CHEQUE ENCASHMENT REQUEST                              │
│  └─ Collect applicant details (name, ID, address, phone)        │
│  └─ Add multiple cheques (number, amount, date, drawer, etc.)   │
│  └─ Upload supporting documents/attachments                     │
│  └─ Collect declarant info (who's signing the declaration)      │
│  └─ Accept terms & conditions                                   │
│  └─ Persist request with all linked data                        │
└─────────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────────┐
│  STEP 3: OFFICIAL USE (APPROVALS)                               │
│  └─ Collect drawer verification (confirmed with, status)        │
│  └─ Account confirmation (account status)                       │
│  └─ Multi-level approval signatures:                            │
│     - Checked By (Name + Signature + Date)                      │
│     - Head of Trade Finance (Name + Signature + Date)           │
│     - In Charge Finance (Name + Signature + Date)               │
│     - CEO (Name + Signature + Date)                             │
│     - Paid By (Name + Signature only)                           │
│  └─ Submit completed official use record                        │
└─────────────────────────────────────────────────────────────────┘
                           ↓
                    ✅ SUBMISSION COMPLETE
            (User can view transaction history)
```

### Bond Request Wizard

The bond request wizard is a separate guided workflow with **two main steps**:

```
┌─────────────────────────────────────────────────────────────────┐
│  STEP 1: APPLICATION DETAILS                                     │
│  └─ Capture principal/applicant details                         │
│  └─ Capture beneficiary / procuring entity details              │
│  └─ Enter guarantee amount in figures and words                 │
│  └─ Select bond type(s) and specify any "Other" type           │
│  └─ Enter tender reference and guarantee effective/expiry dates │
│  └─ Collect two authorized signatories and file-based signatures │
│  └─ Upload supporting bond documents                            │
└─────────────────────────────────────────────────────────────────┘
                           ↓
┌─────────────────────────────────────────────────────────────────┐
│  STEP 2: COUNTER GUARANTEE / INDEMNITY                          │
│  └─ Display legally worded indemnity text                       │
│  └─ Capture indemnity date and authorized signatory names       │
│  └─ Collect indemnity signatures and company seal/stamp         │
│  └─ Submit the completed bond application                       │
└─────────────────────────────────────────────────────────────────┘
                           ↓
                    ✅ SUBMISSION COMPLETE
            (User is returned to the Onboarding Portal)
```

---

## 📊 DETAILED FEATURE BREAKDOWN

### **STEP 1: CLIENT REGISTRATION**

#### Purpose
Onboard a new client or update existing client profile with KYC verification.

#### Data Collection

| Field | Type | Required | Validation | Notes |
|-------|------|----------|-----------|-------|
| **Client Type** | Enum | ✓ | Individual \| SME \| Corporate \| Government | Dropdown selection |
| **Company/Business Name** | String | ✓ | 2-100 chars | Changes label based on client type |
| **KRA PIN** | String | ✓ | 11 chars (Kenya format) | Alphanumeric |
| **Business Registration** | String | Conditional | Required if SME/Corporate | File upload |
| **Contact Person Name** | String | ✓ | 2-100 chars | Contact representative |
| **Contact Email** | String | ✓ | Valid email | |
| **Contact Phone** | String | ✓ | 10+ digits | Kenya format: +254... |
| **ID/Passport Number** | String | ✓ | 6-20 chars | Identifier for individual |
| **Gender** | Enum | ✓ | Male \| Female \| Other | Dropdown |
| **Physical Address** | String | ✓ | 10-200 chars | Full mailing address |

#### KYC Document Uploads

| Document Type | Accepted Formats | Size Limit | Required | Purpose |
|---|---|---|---|---|
| ID Front | JPG, PNG, PDF | 5MB | ✓ | Identity verification |
| ID Back | JPG, PNG, PDF | 5MB | ✓ | Complete ID capture |
| Passport | JPG, PNG, PDF | 5MB | Individual only | Alternative ID |
| Certificate | PDF, DOCX | 5MB | Corporate only | Registration certificate |

#### IPRS Verification
- **Integration**: Kenya IPRS (Identity and Passport Records Service)
- **Action**: Real-time verification of national ID number
- **Response**: Returns verified name, DOB, gender
- **UI Indicator**: Show verification badge ✓ if successful

#### UI Elements
- **Search Existing Clients**: Quick lookup by name/phone before creating new
- **Form Validation**: Real-time email/phone validation
- **Document Preview**: Thumbnail preview before upload
- **Progress Indicator**: Show "Step 1 of 3"

#### Navigation
- Submit → Auto-navigate to Step 2
- Back button (optional) → Return to landing page

---

### **STEP 2: CHEQUE ENCASHMENT REQUEST**

#### Purpose
Collect comprehensive cheque encashment request with multiple cheque entries and supporting documents.

#### Section A: Applicant Information

| Field | Type | Required | Validation |
|-------|------|----------|-----------|
| **Applicant Name** | String | ✓ | 2-100 chars |
| **ID Number** | String | ✓ | 6-20 chars |
| **Postal Address** | String | ✓ | 10-200 chars |
| **Phone Number** | String | ✓ | 10+ digits |

#### Section B: Purpose of Encashment
| Field | Type | Required |
|-------|------|----------|
| **Purpose** | String | ✓ | Free text, 10-500 chars |

#### Section C: Cheque Details (Repeating)

Users can add **one or more cheques**. Each cheque requires:

| Field | Type | Required | Validation |
|-------|------|----------|-----------|
| **Cheque Number** | String | ✓ | 6-12 digits |
| **Amount** | Decimal | ✓ | > 0, 2 decimals |
| **Cheque Date** | Date | ✓ | Valid date (not future) |
| **Drawer** | String | ✓ | 2-100 chars (who issued cheque) |
| **Bank** | String | ✓ | 2-50 chars |
| **Branch** | String | ✓ | 2-50 chars |
| **Payee** | String | ✓ | 2-100 chars (who receives payment) |

**UI Feature**: "Add Another Cheque" button after each cheque → creates new repeating section  
**Min Requirement**: At least 1 valid cheque required  
**Delete Cheque**: Each cheque has delete button (except if only one)

#### Section D: Attachments/Supporting Documents

- **Multiple file upload** (JPG, PNG, PDF, DOCX, XLSX)
- **Max file size**: 5MB per file
- **Max total**: 50MB
- **UI**: List uploaded files with delete option
- **Optional but recommended**: Supporting docs like invoices, agreements

#### Section E: Declarant Information

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| **Declarant Name** | String | ✓ | Person making the declaration |
| **Declarant Role** | String | ✓ | Title/position (Director, Manager, etc.) |
| **Declarant Date** | Date | ✓ | When declaration is made |

**Purpose**: Legal declaration that information is true and correct

#### Section F: Terms & Conditions

| Element | Type |
|---------|------|
| **Display full T&C text** | Scrollable text area |
| **Accept checkbox** | ✓ Required before submit |

#### Validation Rules
```
✓ All applicant fields filled
✓ At least 1 valid cheque with complete data
✓ All cheque amounts > 0
✓ Cheque dates valid (not in future)
✓ Attachments total size < 50MB
✓ Declarant fields all filled
✓ Terms & Conditions checked
```

#### File Uploads
- **Save location**: `wwwroot/uploads/bonds/CE_Attach/{RequestId}/`
- **Filename pattern**: `{DocumentType}_{Timestamp}_{OriginalFilename}`

#### Navigation
- Submit → Save to database → Navigate to Step 3
- Back → Return to Step 1
- Edit mode: If request exists, pre-populate all fields

---

### **STEP 3: OFFICIAL USE (MULTI-LEVEL APPROVALS)**

#### Purpose
Final approval workflow with 5 levels of signing authority.

#### Section A: Drawer Verification

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| **Confirmed With** | String | ✓ | Name of drawer contacted |
| **Designation** | String | ✓ | Drawer's job title |
| **Building/Street** | String | ✓ | Physical location |
| **Drawer Status** | Enum | ✓ | Active \| Suspended \| Closed \| Other |
| **Reason for Payment** | String | ✓ | Why payment is being made (50-300 chars) |

#### Section B: Account Confirmation

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| **Confirmed By** | String | ✓ | Account holder name |
| **Account Status** | Enum | ✓ | Active \| Dormant \| Suspended \| Closed |

#### Section C: Approval Chain (5 Signatories)

Each signatory requires:
- **Name** (printed)
- **Signature** (digital capture/handwriting)
- **Date**

```
1️⃣ CHECKED BY
   ├─ Name: [Text input]
   ├─ Signature: [Signature pad or image upload]
   └─ Date: [Date picker]

2️⃣ HEAD OF TRADE FINANCE
   ├─ Name: [Text input]
   ├─ Signature: [Signature pad or image upload]
   └─ Date: [Date picker]

3️⃣ IN CHARGE FINANCE
   ├─ Name: [Text input]
   ├─ Signature: [Signature pad or image upload]
   └─ Date: [Date picker]

4️⃣ CEO
   ├─ Name: [Text input]
   ├─ Signature: [Signature pad or image upload]
   └─ Date: [Date picker]

5️⃣ PAID BY
   ├─ Name: [Text input]
   └─ Signature: [Signature pad or image upload]
   └─ (No date for Paid By)
```

#### Signature Capture Options
1. **Digital Signature Pad**: Real-time drawing on tablet/touch device
2. **Image Upload**: Upload pre-captured signature
3. **Name as Signature**: Accept typed name if no signature available

#### Signature Storage
- **Format**: PNG image (canvas export)
- **Location**: `wwwroot/uploads/signatures/OfficialUse_{Role}_{Timestamp}.png`
- **Database**: Store filename reference in `OfficialUseRecords` table

#### Validation Rules
```
✓ All drawer verification fields filled
✓ Account confirmation fields filled
✓ All 5 signatories have: Name + Signature + Date
  (Paid By only needs Name + Signature)
✓ Dates are valid and chronological (optional enforcement)
```

#### Navigation
- Submit → Save official use record → Show success message
- Show transaction summary with all captured data
- Option to: View full transaction history / Export PDF / Start new request

---

## 🗄️ DATA MODEL & DATABASE

### Core Tables (SQL Server)

```sql
-- Clients (from Step 1)
CREATE TABLE dbo.Clients (
    Id INT PRIMARY KEY IDENTITY,
    ClientType INT NOT NULL,              -- 1=Individual, 2=SME, 3=Corporate, 4=Government
    CompanyName NVARCHAR(100) NOT NULL,
    KraPin NVARCHAR(11) NOT NULL UNIQUE,
    BusinessRegistration NVARCHAR(50),
    ContactPerson NVARCHAR(100) NOT NULL,
    ContactEmail NVARCHAR(100) NOT NULL,
    ContactPhone NVARCHAR(20) NOT NULL,
    IdNumber NVARCHAR(20) NOT NULL,
    Gender INT NOT NULL,                  -- 1=Male, 2=Female, 3=Other
    PhysicalAddress NVARCHAR(200) NOT NULL,
    IprsVerified BIT DEFAULT 0,
    IprsVerifiedDate DATETIME,
    CreatedAt DATETIME DEFAULT GETDATE(),
    CreatedBy NVARCHAR(100),
    UpdatedAt DATETIME,
    UpdatedBy NVARCHAR(100)
);

-- ChequeEncashmentRequests (from Step 2 - main record)
CREATE TABLE dbo.ChequeEncashmentRequests (
    Id INT PRIMARY KEY IDENTITY,
    ClientId INT,                         -- Optional FK to Clients table
    ApplicantName NVARCHAR(100) NOT NULL,
    IdNumber NVARCHAR(20) NOT NULL,
    PostalAddress NVARCHAR(200) NOT NULL,
    Phone NVARCHAR(20) NOT NULL,
    Purpose NVARCHAR(500) NOT NULL,
    DeclarantName NVARCHAR(100) NOT NULL,
    DeclarantRole NVARCHAR(100) NOT NULL,
    DeclarantDate DATETIME NOT NULL,
    TermsAccepted BIT NOT NULL,
    CreatedAt DATETIME DEFAULT GETDATE(),
    CreatedBy NVARCHAR(100),
    UpdatedAt DATETIME,
    UpdatedBy NVARCHAR(100),
    FOREIGN KEY (ClientId) REFERENCES dbo.Clients(Id)
);

-- ChequeEncashmentCheques (from Step 2 - repeating)
CREATE TABLE dbo.ChequeEncashmentCheques (
    Id INT PRIMARY KEY IDENTITY,
    RequestId INT NOT NULL,
    ChequeNumber NVARCHAR(20) NOT NULL,
    Amount DECIMAL(15,2) NOT NULL,
    ChequeDate DATE NOT NULL,
    Drawer NVARCHAR(100) NOT NULL,
    Bank NVARCHAR(50) NOT NULL,
    Branch NVARCHAR(50) NOT NULL,
    Payee NVARCHAR(100) NOT NULL,
    FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id) ON DELETE CASCADE
);

-- ChequeEncashmentAttachments (from Step 2 - files)
CREATE TABLE dbo.ChequeEncashmentAttachments (
    Id INT PRIMARY KEY IDENTITY,
    RequestId INT NOT NULL,
    FilePath NVARCHAR(500) NOT NULL,
    FileName NVARCHAR(255) NOT NULL,
    ContentType NVARCHAR(100) NOT NULL,
    FileSize INT,
    CreatedAt DATETIME DEFAULT GETDATE(),
    FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id) ON DELETE CASCADE
);

-- OfficialUseRecords (from Step 3 - approvals)
CREATE TABLE dbo.OfficialUseRecords (
    Id INT PRIMARY KEY IDENTITY,
    RequestId INT NOT NULL,
    CheckedBy NVARCHAR(100) NOT NULL,
    CheckedSignature NVARCHAR(500),      -- Image filename
    CheckedDate DATETIME NOT NULL,
    ConfirmedWith NVARCHAR(100) NOT NULL,
    Designation NVARCHAR(100) NOT NULL,
    BuildingStreet NVARCHAR(200) NOT NULL,
    DrawerStatus INT NOT NULL,           -- 1=Active, 2=Suspended, 3=Closed, 4=Other
    ReasonForPayment NVARCHAR(300) NOT NULL,
    AccountConfirmedBy NVARCHAR(100) NOT NULL,
    AccountStatus INT NOT NULL,          -- 1=Active, 2=Dormant, 3=Suspended, 4=Closed
    HeadOfTradeFinance NVARCHAR(100) NOT NULL,
    HeadOfTradeSignature NVARCHAR(500),  -- Image filename
    HeadOfTradeDate DATETIME NOT NULL,
    InChargeFinance NVARCHAR(100) NOT NULL,
    InChargeFinanceSignature NVARCHAR(500), -- Image filename
    InChargeFinanceDate DATETIME NOT NULL,
    CEO NVARCHAR(100) NOT NULL,
    CEOSignature NVARCHAR(500),          -- Image filename
    CEODate DATETIME NOT NULL,
    PaidByName NVARCHAR(100) NOT NULL,
    PaidBySignature NVARCHAR(500),       -- Image filename
    CreatedAt DATETIME DEFAULT GETDATE(),
    CreatedBy NVARCHAR(100),
    FOREIGN KEY (RequestId) REFERENCES dbo.ChequeEncashmentRequests(Id) ON DELETE CASCADE
);
```

### Data Flow (Backend API Integration)

```
MAUI App                          OnwardsSwift.API Backend
─────────────────────────────────────────────────────────

Step 1:
  POST /api/clients              → Save Client + KYC files
  GET  /api/iprs/{idNumber}      → IPRS verification
                 ← Returns verified data

Step 2:
  POST /api/cheques/request      → Save ChequeEncashmentRequest
  POST /api/cheques/items        → Save individual cheques
  POST /api/cheques/attachments  → Upload files
                 ← Returns RequestId

Step 3:
  POST /api/officialuse/{id}     → Save OfficialUseRecords
                 ← Returns success

View History:
  GET  /api/transactions         → List all submissions
  GET  /api/transactions/{id}    → View full 3-step profile
```

---

## 🏗️ ARCHITECTURE (MAUI App)

### Project Structure

```
OnwardsSwift.MAUI/
├── OnwardsSwift.MAUI.csproj
├── MauiProgram.cs              -- Dependency injection, route registration
├── App.xaml / App.xaml.cs      -- App shell configuration
├── AppShell.xaml               -- Navigation routes
│
├── Models/
│   ├── Client.cs               -- Step 1 data model
│   ├── ChequeEncashmentRequest.cs -- Step 2 main model
│   ├── ChequeItem.cs           -- Step 2 repeating cheques
│   ├── Attachment.cs           -- Step 2 file attachment
│   └── OfficialUseRecord.cs    -- Step 3 approval model
│
├── Services/
│   ├── IApiService.cs          -- HTTP client interface
│   ├── ApiService.cs           -- HTTP implementation
│   ├── IWizardService.cs       -- Business logic
│   ├── WizardService.cs        -- Orchestrates 3 steps
│   ├── IIprsService.cs         -- IPRS verification
│   └── StorageService.cs       -- File management
│
├── ViewModels/
│   ├── WizardViewModel.cs      -- Shared wizard state
│   ├── Step1ViewModel.cs       -- Client registration logic
│   ├── Step2ViewModel.cs       -- Cheque encashment logic
│   ├── Step3ViewModel.cs       -- Approval signatures logic
│   └── HistoryViewModel.cs     -- Transaction history
│
├── Views/
│   ├── WizardPage.xaml / .cs   -- Main wizard container
│   ├── Step1Page.xaml / .cs    -- Client registration UI
│   ├── Step2Page.xaml / .cs    -- Cheque encashment UI
│   ├── Step3Page.xaml / .cs    -- Official use UI
│   ├── HistoryPage.xaml / .cs  -- Transaction history
│   └── DetailPage.xaml / .cs   -- View full transaction
│
├── Converters/
│   ├── EnumToStringConverter.cs
│   ├── BytesToImageSourceConverter.cs
│   └── DateFormatConverter.cs
│
├── Behaviors/
│   ├── NumericValidationBehavior.cs
│   ├── PhoneNumberBehavior.cs
│   └── MaxLengthValidator.cs
│
├── Controls/
│   ├── SignaturePadView.xaml / .cs  -- Custom signature capture
│   ├── FilePickerButton.xaml / .cs  -- File upload control
│   └── ChequeSummaryCard.xaml / .cs -- Cheque display card
│
└── Resources/
    ├── Styles.xaml             -- Global styling (Navy theme)
    ├── Colors.xaml             -- #002D72 theme colors
    └── Strings.xaml            -- Localization strings
```

### Key Technologies

| Component | Technology |
|-----------|-----------|
| **UI Framework** | MAUI (XAML + C#) |
| **HTTP Client** | HttpClient with Polly retry policies |
| **Local Data** | SQLite (offline draft support) |
| **File Access** | FilePicker, MediaPicker (MAUI plugins) |
| **Signature Pad** | SkiaSharp for drawing |
| **Image Handling** | CommunityToolkit.Mvvm |
| **Date Picker** | MAUI DatePicker control |
| **Validation** | FluentValidation library |
| **Logging** | Serilog |
| **State Management** | MVVM + ObservableCollections |

---

## 🎨 UI/UX DESIGN

### Common Elements Across All Steps

**Header**
- Back button (exit wizard)
- Progress indicator: "Step X of 3"
- Step title

**Footer**
- Navigation buttons: "< Previous" and "Next >"
- Submit button on Step 3: "Complete Submission"

**Theme**
- Primary color: Navy (#002D72)
- Accent color: Light blue (#4A90E2)
- Background: Light gray (#F5F5F5)
- Text: Dark gray (#333333)
- Success: Green (#27AE60)
- Error: Red (#E74C3C)

### Step 1: Client Registration

```
┌─────────────────────────────────────────────┐
│ STEP 1 OF 3: CLIENT REGISTRATION            │
│ [← Back]                                    │
├─────────────────────────────────────────────┤
│                                             │
│ 📋 SEARCH EXISTING CLIENT (Optional)        │
│ ┌─────────────────────────────────────────┐ │
│ │ Search by Name or Phone...              │ │
│ └─────────────────────────────────────────┘ │
│ [Results dropdown if found]                 │
│                                             │
│ ━ OR CREATE NEW CLIENT ━                    │
│                                             │
│ Client Type *                               │
│ [Dropdown: Individual / SME / Corporate...] │
│                                             │
│ Company/Business Name *                     │
│ [Text input]                                │
│                                             │
│ KRA PIN *                                   │
│ [Text input] [? Verify with IPRS]          │
│ ✓ Verified!                                 │
│                                             │
│ Business Registration (if Corporate)        │
│ [File picker] [Upload icon]                 │
│                                             │
│ Contact Information                         │
│ ┌─────────────────────────────────────────┐ │
│ │ Contact Person *: [_________________]   │ │
│ │ Contact Email *:  [_________________]   │ │
│ │ Contact Phone *:  [_________________]   │ │
│ │ Physical Address *: [________________]  │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ Identification                              │
│ ┌─────────────────────────────────────────┐ │
│ │ ID/Passport Number *: [______________]  │ │
│ │ Gender *: [Dropdown]                    │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ KYC Documents *                             │
│ ┌────────────────┬────────────────┐         │
│ │ ID Front       │ [Upload]       │         │
│ │ [Thumbnail]    │                │         │
│ └────────────────┴────────────────┘         │
│ ┌────────────────┬────────────────┐         │
│ │ ID Back        │ [Upload]       │         │
│ │ [Thumbnail]    │                │         │
│ └────────────────┴────────────────┘         │
│ ┌────────────────┬────────────────┐         │
│ │ Passport       │ [Upload]       │         │
│ │ [Thumbnail]    │                │         │
│ └────────────────┴────────────────┘         │
│                                             │
├─────────────────────────────────────────────┤
│ [Previous]                     [Next →]     │
└─────────────────────────────────────────────┘
```

### Step 2: Cheque Encashment

```
┌─────────────────────────────────────────────┐
│ STEP 2 OF 3: CHEQUE ENCASHMENT REQUEST      │
│ [← Back]                                    │
├─────────────────────────────────────────────┤
│                                             │
│ APPLICANT INFORMATION                       │
│ ┌─────────────────────────────────────────┐ │
│ │ Applicant Name *:      [_____________]  │ │
│ │ ID Number *:           [_____________]  │ │
│ │ Postal Address *:      [_____________]  │ │
│ │ Phone Number *:        [_____________]  │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ PURPOSE OF ENCASHMENT                       │
│ ┌─────────────────────────────────────────┐ │
│ │ [Large text area for purpose...]        │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ CHEQUE DETAILS                              │
│ ┌─────────────────────────────────────────┐ │
│ │ 💳 CHEQUE #1                       [✕]  │ │
│ │ ┌─────────────────────────────────────┐ │ │
│ │ │ Cheque Number *: [______________]  │ │ │
│ │ │ Amount *: [______________]         │ │ │
│ │ │ Cheque Date *: [Date picker]       │ │ │
│ │ │ Drawer *: [______________]         │ │ │
│ │ │ Bank *: [______________]           │ │ │
│ │ │ Branch *: [______________]         │ │ │
│ │ │ Payee *: [______________]          │ │ │
│ │ └─────────────────────────────────────┘ │ │
│ └─────────────────────────────────────────┘ │
│ [+ Add Another Cheque]                      │
│                                             │
│ SUPPORTING DOCUMENTS                        │
│ [📎 Upload Files] (Max 50MB total)         │
│ ✓ invoice.pdf (2.3 MB)          [✕]        │
│ ✓ agreement.docx (1.1 MB)       [✕]        │
│ [+ Add More Files]                          │
│                                             │
│ DECLARANT INFORMATION                       │
│ ┌─────────────────────────────────────────┐ │
│ │ Declarant Name *:    [______________]  │ │
│ │ Declarant Role *:    [______________]  │ │
│ │ Declarant Date *:    [Date picker]     │ │
│ └─────────────────────────────────────────┘ │
│                                             │
│ TERMS & CONDITIONS                          │
│ ┌─────────────────────────────────────────┐ │
│ │ By encashing this cheque, I declare... │ │
│ │ [Scrollable T&C text here...]           │ │
│ │ [Scrollable T&C text here...]           │ │
│ │ [Scrollable T&C text here...]           │ │
│ └─────────────────────────────────────────┘ │
│ ☐ I accept the terms and conditions *      │ │
│                                             │
├─────────────────────────────────────────────┤
│ [Previous]                     [Next →]     │
└─────────────────────────────────────────────┘
```

### Step 3: Official Use (Approvals)

```
┌────────────────────────────────────────────────┐
│ STEP 3 OF 3: OFFICIAL USE (APPROVALS)         │
│ [← Back]                                       │
├────────────────────────────────────────────────┤
│                                                │
│ DRAWER VERIFICATION                            │
│ ┌──────────────────────────────────────────┐   │
│ │ Confirmed With *:      [________________] │   │
│ │ Designation *:         [________________] │   │
│ │ Building/Street *:     [________________] │   │
│ │ Drawer Status *: [Dropdown]             │   │
│ │ Reason for Payment *: [Large text area] │   │
│ └──────────────────────────────────────────┘   │
│                                                │
│ ACCOUNT CONFIRMATION                           │
│ ┌──────────────────────────────────────────┐   │
│ │ Confirmed By *:       [________________]  │   │
│ │ Account Status *:     [Dropdown]          │   │
│ └──────────────────────────────────────────┘   │
│                                                │
│ APPROVAL SIGNATURES (5 Levels)                │
│                                                │
│ 🔏 1. CHECKED BY                               │
│ ├─ Name *: [________________]                  │
│ ├─ Signature *: [🖊 Draw] or [📸 Upload]      │
│ │  [Canvas signature pad area]                │
│ │  [Clear] [Accept]                           │
│ └─ Date *: [Date picker]                      │
│                                                │
│ 🔏 2. HEAD OF TRADE FINANCE                    │
│ ├─ Name *: [________________]                  │
│ ├─ Signature *: [🖊 Draw] or [📸 Upload]      │
│ └─ Date *: [Date picker]                      │
│                                                │
│ 🔏 3. IN CHARGE FINANCE                        │
│ ├─ Name *: [________________]                  │
│ ├─ Signature *: [🖊 Draw] or [📸 Upload]      │
│ └─ Date *: [Date picker]                      │
│                                                │
│ 🔏 4. CEO                                      │
│ ├─ Name *: [________________]                  │
│ ├─ Signature *: [🖊 Draw] or [📸 Upload]      │
│ └─ Date *: [Date picker]                      │
│                                                │
│ 🔏 5. PAID BY                                  │
│ ├─ Name *: [________________]                  │
│ └─ Signature *: [🖊 Draw] or [📸 Upload]      │
│                                                │
├────────────────────────────────────────────────┤
│ [Previous]              [✓ Complete Submission]│
└────────────────────────────────────────────────┘
```

---

## 🔄 USER WORKFLOWS

### Workflow 1: New User - Complete 3-Step Journey

```
1. Launch app
   ↓
2. Landing page → "Start New Request"
   ↓
3. Step 1: Enter client details + KYC docs
   ↓
4. Step 2: Enter applicant + cheques + docs
   ↓
5. Step 3: Enter approvals + 5 signatures
   ↓
6. Success screen: Show request ID + option to view history
```

### Workflow 2: Offline Partial Completion (SQLite)

```
1. User starts filling Step 1
   ↓
2. Network lost → Auto-save to local SQLite
   ↓
3. App shows "Draft saved locally"
   ↓
4. User closes app
   ↓
5. User opens app again → Show "Continue Draft"
   ↓
6. Load from local DB → Resume from Step 1
   ↓
7. Network restored → Submit to backend
```

### Workflow 3: Edit Existing Request

```
1. View transaction history
   ↓
2. Click on cheque encashment request
   ↓
3. System detects no OfficialUseRecord exists
   ↓
4. Show "Continue to Step 3" button
   ↓
5. Jump to Step 3 (skip Steps 1 & 2)
   ↓
6. Complete approvals and submit
```

---

## 📱 MOBILE-SPECIFIC FEATURES

1. **Camera Integration**: Capture ID/document photos directly from camera
2. **Gallery Access**: Pick existing photos from device
3. **Signature Pad**: Touch-based drawing for signatures
4. **Offline Mode**: Draft requests saved to SQLite, sync when online
5. **Push Notifications**: Notify when request approved/rejected (future)
6. **QR Code**: Generate QR for request ID sharing
7. **File Management**: Auto-cleanup of temporary files

---

## 🔐 SECURITY & VALIDATION

### Input Validation
- **Email**: RFC 5322 regex validation
- **Phone**: Kenya format +254 or 0
- **Currency**: Max 15 digits, 2 decimals
- **Date**: No future dates, no dates > 10 years past
- **File upload**: Whitelist extensions (jpg, png, pdf, docx)
- **SQL Injection**: Use parameterized queries only
- **XSS Protection**: HTML encode all user input in views

### Authentication
- Cookie-based (inherited from backend ASP.NET Core)
- 8-hour sliding expiration
- Secure HTTPS only
- Certificate pinning (optional on MAUI)

### Data Storage
- **Sensitive data**: Encrypt before local SQLite storage
- **Files**: Store in app-specific directories (not accessible to other apps)
- **Signatures**: Treated as sensitive, encrypted locally
- **PII Masking**: Mask client IDs in transaction history unless admin

### API Communication
```
All requests:
- Authorization header: Bearer {token}
- Content-Type: application/json
- User-Agent: OnwardsSwift-MAUI/1.0

Error handling:
- 401 → Re-authenticate
- 403 → Show permission denied
- 422 → Show validation errors
- 500 → Retry with exponential backoff (Polly)
```

---

## 🧪 TESTING STRATEGY

### Unit Tests (xUnit)
- ViewModel logic (validation, state changes)
- Service business logic (cheque calculations, status checks)
- Converter logic (enum to string, date formatting)

### Integration Tests
- API calls (mock HttpClient)
- Database operations (in-memory SQLite)
- File operations (temp directory)

### UI Tests (Appium / MAUI UITest)
- Navigate through all 3 steps
- Fill form with valid/invalid data
- Upload files
- Capture signatures
- Submit and view history

### Manual Testing Scenarios
1. Valid happy path (complete all 3 steps)
2. Form validation errors (submit with missing fields)
3. Network failures (retry mechanism)
4. Large file uploads (> 50MB total)
5. Offline draft + online sync
6. iOS/Android/Windows platform-specific

---

## 📅 IMPLEMENTATION TIMELINE

### **Phase 1: Foundation & Architecture (2-3 weeks)**
- Project setup (.NET MAUI 8.0)
- DI container configuration
- Navigation infrastructure
- HTTP client setup with retry policies
- Local SQLite setup
- **Deliverables**: Runnable app shell with empty pages

### **Phase 2: Step 1 Implementation (2-3 weeks)**
- Client model & ViewModel
- Step1 Page (XAML + C#)
- Form validation
- IPRS integration
- KYC file upload service
- **Deliverables**: Complete Step 1 with IPRS verification

### **Phase 3: Step 2 Implementation (3-4 weeks)**
- ChequeEncashmentRequest model
- Step2 Page with repeating cheques
- File upload service (supporting docs)
- File picker integration
- Terms & conditions display
- **Deliverables**: Complete Step 2 with dynamic cheque addition

### **Phase 4: Step 3 Implementation (2-3 weeks)**
- OfficialUseRecord model
- Step3 Page (5 signatories)
- Signature pad control (SkiaSharp)
- Signature capture & storage
- **Deliverables**: Complete Step 3 with multi-level approvals

### **Phase 5: History & Review Features (1-2 weeks)**
- Transaction history page
- Detail view for past requests
- Edit workflow (jump to Step 3)
- **Deliverables**: View transaction history, drill into details

### **Phase 6: Offline & Sync (2 weeks)**
- SQLite local storage
- Offline draft management
- Sync queue when online
- **Deliverables**: Draft locally, sync when network available

### **Phase 7: Testing & Refinement (2-3 weeks)**
- Unit tests (60%+ coverage)
- Integration tests
- UI testing (iOS/Android/Windows)
- Performance optimization
- **Deliverables**: Test suite, bug fixes

### **Phase 8: Deployment (1 week)**
- App signing (iOS, Android, Windows)
- Store submission (Apple, Google Play)
- Release notes & documentation
- **Deliverables**: Production apps ready

---

## ⏱️ TOTAL PROJECT TIMELINE

| Phase | Duration | Start | End |
|-------|----------|-------|-----|
| Phase 1 | 2-3 weeks | Week 1 | Week 3 |
| Phase 2 | 2-3 weeks | Week 3 | Week 6 |
| Phase 3 | 3-4 weeks | Week 6 | Week 10 |
| Phase 4 | 2-3 weeks | Week 10 | Week 13 |
| Phase 5 | 1-2 weeks | Week 13 | Week 14 |
| Phase 6 | 2 weeks | Week 14 | Week 16 |
| Phase 7 | 2-3 weeks | Week 16 | Week 19 |
| Phase 8 | 1 week | Week 19 | Week 20 |
| **TOTAL** | **20-21 weeks** | Week 1 | **Week 20** |

**Estimated Start**: June 2026  
**Estimated Completion**: Late October 2026  

**Buffer**: Additional 2-3 weeks for unforeseen issues (API changes, platform issues, etc.)

---

## 💼 RESOURCE REQUIREMENTS

### Development Team
- **1 Senior .NET MAUI Developer** (Full-time, 20 weeks)
- **1 Backend API Developer** (Part-time, 4-5 weeks for API enhancements/fixes)
- **1 QA/Tester** (Part-time, Weeks 7-20)

### Infrastructure
- **Development**: Visual Studio 2022, .NET 8.0, MAUI templates
- **Testing**: iOS simulator, Android emulator, Windows environment
- **CI/CD**: GitHub Actions or Azure DevOps
- **App Distribution**: Azure App Center, Microsoft Store, Google Play, Apple App Store

### Dependencies
- **NuGet Packages**: 
  - `CommunityToolkit.Mvvm` (state management)
  - `CommunityToolkit.Diagnostics` (validation)
  - `Polly` (retry policies)
  - `SkiaSharp` (signature drawing)
  - `SQLite-net-pcl` (local database)
  - `FluentValidation` (form validation)
  - `Serilog` (logging)

---

## 🎯 SUCCESS CRITERIA

- ✅ All 3 steps functional and fully integrated
- ✅ 95%+ form validation accuracy
- ✅ < 2 second average load time per screen
- ✅ Offline mode working (draft + sync)
- ✅ < 100MB app size
- ✅ 60%+ code coverage (unit tests)
- ✅ iOS & Android store approval
- ✅ Zero critical bugs in UAT

---

## 🚀 FUTURE ENHANCEMENTS

1. **Digital Signature Standards**: Support PKCS#7 certificates
2. **Push Notifications**: Real-time approval status updates
3. **Biometric Authentication**: Fingerprint/Face ID login
4. **Multi-Language**: Localization (Swahili, French, etc.)
5. **Dark Mode**: Support system theme preference
6. **Accessibility**: WCAG 2.1 AA compliance
7. **Analytics**: Usage tracking & performance monitoring
8. **API v2**: GraphQL backend for optimized queries

---

## 📞 CONTACT & SUPPORT

For questions about this specification or implementation details:
- Review the linked API documentation in OnwardsSwift.API
- Check SQL migrations for exact schema
- Refer to CLAUDE.md for build & run commands


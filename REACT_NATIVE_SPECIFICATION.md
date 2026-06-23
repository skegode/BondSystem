# React Native OnwardsSwift Mobile App - Complete Specification

**Project**: OnwardsSwift Mobile Wizards (iOS & Android)
**Platform**: React Native (Expo & Native Build)
**Date**: 2026-06-08

---

## 📋 EXECUTIVE SUMMARY

This document outlines a complete implementation of the OnwardsSwift onboarding and bond request wizards as a React Native cross-platform mobile application. The app processes client registrations, cheque encashments, official approvals, and bank guarantee applications—integrating with the existing OnwardsSwift.API backend.

---

## 🎯 WIZARD OVERVIEW

The React Native app contains two distinct guided workflows:

1. **Client Onboarding Wizard** — a 3-step process for client onboarding, cheque encashment requests, and official approval capture.
2. **Bond Request Wizard** — a 2-step process for submitting bank guarantee applications and completing indemnity sections.

Both wizards integrate with the OnwardsSwift.API backend and support offline-first architecture with local data persistence.

---

## 📊 DETAILED FEATURE BREAKDOWN

*(Identical features to MAUI spec; see original document for Step 1, 2, 3 details)*

### **STEP 1: CLIENT REGISTRATION**

**Data Collection**: Client Type, Company/Business Name, KRA PIN, Contact Person, Email, Phone, ID/Passport, Gender, Physical Address

**KYC Documents**: ID Front/Back, Passport, Certificate (with file upload)

**IPRS Verification**: Real-time Kenya national ID verification with UI badge

**UI Elements**: Search existing clients, real-time validation, document preview

---

### **STEP 2: CHEQUE ENCASHMENT REQUEST**

**Section A**: Applicant Information (Name, ID, Address, Phone)

**Section B**: Purpose of Encashment (text)

**Section C**: Repeating Cheques (Cheque Number, Amount, Date, Drawer, Bank, Branch, Payee)

**Section D**: Attachments/Supporting Documents (multiple file upload, max 50MB total)

**Section E**: Declarant Information (Name, Role, Date)

**Section F**: Terms & Conditions acceptance

---

### **STEP 3: OFFICIAL USE (APPROVALS)**

**Section A**: Drawer Verification (Confirmed With, Designation, Building/Street, Status, Reason for Payment)

**Section B**: Account Confirmation (Confirmed By, Account Status)

**Section C**: 5-Level Approval Signatures:
- Checked By (Name + Signature + Date)
- Head of Trade Finance (Name + Signature + Date)
- In Charge Finance (Name + Signature + Date)
- CEO (Name + Signature + Date)
- Paid By (Name + Signature only)

---

## 🏗️ ARCHITECTURE (React Native)

### Project Structure

```
OnwardsSwift-Mobile/
├── package.json
├── app.json                    -- Expo configuration
├── eas.json                    -- EAS Build configuration
├── babel.config.js
├── tsconfig.json
│
├── src/
│   ├── api/
│   │   ├── client.ts           -- Axios HTTP client
│   │   ├── endpoints.ts        -- API routes
│   │   ├── types.ts            -- API response types
│   │   └── interceptors.ts     -- Auth/error handling
│   │
│   ├── services/
│   │   ├── storageService.ts   -- AsyncStorage wrapper
│   │   ├── fileService.ts      -- File upload/access
│   │   ├── iprsService.ts      -- IPRS integration
│   │   ├── wizardService.ts    -- Wizard business logic
│   │   └── cacheService.ts     -- Local data cache
│   │
│   ├── store/
│   │   ├── index.ts            -- Redux store configuration
│   │   ├── slices/
│   │   │   ├── authSlice.ts    -- Auth state
│   │   │   ├── wizardSlice.ts  -- Wizard state (shared)
│   │   │   ├── step1Slice.ts   -- Client registration state
│   │   │   ├── step2Slice.ts   -- Cheque encashment state
│   │   │   ├── step3Slice.ts   -- Official use state
│   │   │   └── historySlice.ts -- Transaction history
│   │   └── hooks.ts            -- useAppDispatch, useAppSelector
│   │
│   ├── screens/
│   │   ├── onboarding/
│   │   │   ├── WizardScreen.tsx       -- Wizard container/navigation
│   │   │   ├── Step1Screen.tsx        -- Client registration UI
│   │   │   ├── Step2Screen.tsx        -- Cheque encashment UI
│   │   │   ├── Step3Screen.tsx        -- Official use UI
│   │   │   └── SuccessScreen.tsx      -- Completion confirmation
│   │   │
│   │   ├── bond/
│   │   │   ├── BondWizardScreen.tsx   -- Bond wizard container
│   │   │   ├── BondStep1Screen.tsx    -- Bond application details
│   │   │   ├── BondStep2Screen.tsx    -- Bond indemnity/counter-guarantee
│   │   │   └── BondSuccessScreen.tsx  -- Completion confirmation
│   │   │
│   │   ├── history/
│   │   │   ├── HistoryScreen.tsx      -- Transaction history list
│   │   │   ├── DetailScreen.tsx       -- View full transaction
│   │   │   └── ReceiptScreen.tsx      -- Print/share receipt
│   │   │
│   │   └── auth/
│   │       ├── LoginScreen.tsx        -- User login
│   │       └── ProfileScreen.tsx      -- User profile
│   │
│   ├── components/
│   │   ├── common/
│   │   │   ├── HeaderBar.tsx          -- Navigation header
│   │   │   ├── StepIndicator.tsx      -- Progress display
│   │   │   ├── Button.tsx             -- Custom button
│   │   │   ├── Input.tsx              -- Text input wrapper
│   │   │   ├── Picker.tsx             -- Dropdown wrapper
│   │   │   └── LoadingSpinner.tsx     -- Loading indicator
│   │   │
│   │   ├── forms/
│   │   │   ├── ClientForm.tsx         -- Step 1 form component
│   │   │   ├── ChequeForm.tsx         -- Step 2 form component
│   │   │   ├── ChequeItem.tsx         -- Individual cheque card
│   │   │   ├── ApprovalForm.tsx       -- Step 3 approvals
│   │   │   ├── SignatureCapture.tsx   -- Signature pad control
│   │   │   └── TermsCheckbox.tsx      -- T&C acceptance
│   │   │
│   │   ├── upload/
│   │   │   ├── FilePickerButton.tsx   -- File selection
│   │   │   ├── FileList.tsx           -- Selected files display
│   │   │   └── UploadProgress.tsx     -- Upload status
│   │   │
│   │   └── modal/
│   │       ├── ConfirmDialog.tsx      -- Yes/No confirmation
│   │       ├── ErrorAlert.tsx         -- Error message
│   │       └── SuccessToast.tsx       -- Success notification
│   │
│   ├── hooks/
│   │   ├── useWizardState.ts          -- Wizard state management
│   │   ├── useApi.ts                  -- HTTP requests
│   │   ├── useFileUpload.ts           -- File upload logic
│   │   ├── useSignature.ts            -- Signature pad control
│   │   ├── useForm.ts                 -- Form validation
│   │   ├── useAuth.ts                 -- Authentication
│   │   └── useOfflineSync.ts          -- Offline data sync
│   │
│   ├── utils/
│   │   ├── validation.ts              -- Form validators
│   │   ├── formatters.ts              -- Date/currency formatting
│   │   ├── constants.ts               -- App constants
│   │   ├── logger.ts                  -- Logging utility
│   │   └── storage-keys.ts            -- AsyncStorage keys
│   │
│   ├── types/
│   │   ├── client.ts                  -- Client types
│   │   ├── cheque.ts                  -- Cheque request types
│   │   ├── approval.ts                -- Approval record types
│   │   ├── bond.ts                    -- Bond request types
│   │   ├── api.ts                     -- API response types
│   │   └── navigation.ts              -- React Navigation types
│   │
│   ├── navigation/
│   │   ├── RootNavigator.tsx          -- Root stack navigator
│   │   ├── WizardNavigator.tsx        -- Wizard tab/stack navigator
│   │   ├── AuthNavigator.tsx          -- Auth flow navigator
│   │   └── LinkingConfiguration.ts    -- Deep linking setup
│   │
│   ├── styles/
│   │   ├── theme.ts                   -- Theme colors & spacing
│   │   ├── typography.ts              -- Font sizes & families
│   │   └── global.ts                  -- Global styles
│   │
│   └── App.tsx                        -- Entry point
│
├── assets/
│   ├── images/
│   ├── icons/
│   └── fonts/
│
└── __tests__/
    ├── components/
    ├── hooks/
    ├── services/
    └── utils/
```

### Key Technologies

| Component                  | Technology                                   |
| -------------------------- | -------------------------------------------- |
| **Framework**        | React Native 0.74+ with Expo                 |
| **Language**         | TypeScript 5.0+                              |
| **State Management** | Redux Toolkit + RTK Query                    |
| **HTTP Client**      | Axios with interceptors                      |
| **Local Storage**    | AsyncStorage (Expo)                          |
| **Navigation**       | React Navigation 6.x                         |
| **Forms**            | React Hook Form + Zod validation             |
| **File Access**      | Expo Document Picker + Media Library         |
| **Signature Pad**    | react-native-svg + gesture-handler           |
| **Image Handling**   | Expo Image with fast-image caching           |
| **UI Components**    | React Native Paper + custom components       |
| **Date Picker**      | react-native-date-picker                     |
| **Logging**          | React Native Firebase Analytics + Sentry     |
| **Testing**          | Jest + React Native Testing Library           |
| **API Documentation**| Swagger/Expo SDK docs                        |

### Dependencies (package.json)

```json
{
  "name": "onwards-swift-mobile",
  "version": "1.0.0",
  "main": "node_modules/expo/AppEntry.js",
  "scripts": {
    "start": "expo start",
    "android": "expo run:android",
    "ios": "expo run:ios",
    "build-android": "eas build --platform android",
    "build-ios": "eas build --platform ios",
    "test": "jest",
    "lint": "eslint src/**/*.ts{,x}"
  },
  "dependencies": {
    "react": "^18.2.0",
    "react-native": "^0.74.0",
    "expo": "^51.0.0",
    "@react-navigation/native": "^6.1.0",
    "@react-navigation/bottom-tabs": "^6.5.0",
    "@react-navigation/stack": "^6.3.0",
    "redux": "^4.2.1",
    "@reduxjs/toolkit": "^1.9.5",
    "react-redux": "^8.1.3",
    "axios": "^1.6.0",
    "react-hook-form": "^7.50.0",
    "zod": "^3.22.0",
    "@hookform/resolvers": "^3.3.0",
    "react-native-paper": "^5.11.0",
    "react-native-gesture-handler": "^2.14.0",
    "@react-native-community/hooks": "^3.0.0",
    "react-native-svg": "^14.0.0",
    "expo-document-picker": "^13.0.0",
    "expo-media-library": "^15.5.0",
    "expo-image-picker": "^14.7.0",
    "expo-file-system": "^16.0.0",
    "react-native-fast-image": "^8.6.0",
    "react-native-date-picker": "^4.6.0",
    "expo-secure-store": "^13.0.0",
    "lodash": "^4.17.21"
  },
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@types/react-native": "^0.74.0",
    "@types/jest": "^29.5.0",
    "jest": "^29.7.0",
    "@testing-library/react-native": "^12.0.0",
    "typescript": "^5.2.0",
    "eslint": "^8.52.0",
    "@typescript-eslint/eslint-plugin": "^6.10.0"
  }
}
```

---

## 🎨 UI/UX DESIGN

### Theme Configuration

```typescript
// src/styles/theme.ts
export const theme = {
  colors: {
    primary: '#002D72',      // Navy
    secondary: '#4A90E2',    // Light Blue
    success: '#27AE60',      // Green
    error: '#E74C3C',        // Red
    warning: '#F39C12',      // Orange
    background: '#F5F5F5',   // Light Gray
    surface: '#FFFFFF',      // White
    text: '#333333',         // Dark Gray
    textSecondary: '#666666',
    border: '#DDDDDD',
    disabled: '#CCCCCC'
  },
  spacing: {
    xs: 4,
    sm: 8,
    md: 16,
    lg: 24,
    xl: 32
  },
  borderRadius: {
    sm: 4,
    md: 8,
    lg: 12,
    xl: 16
  },
  typography: {
    fontSize: {
      xs: 12,
      sm: 14,
      md: 16,
      lg: 18,
      xl: 20,
      xxl: 24
    },
    fontWeight: {
      light: '300',
      regular: '400',
      medium: '500',
      semibold: '600',
      bold: '700'
    }
  }
};
```

### Screen Layouts

All screens follow this structure:

```
┌─────────────────────────────────┐
│ Header (Back + Title + Progress) │
├─────────────────────────────────┤
│                                 │
│ Content (ScrollView)            │
│ ┌───────────────────────────┐   │
│ │ Form fields or content    │   │
│ │ ...                       │   │
│ │ ...                       │   │
│ └───────────────────────────┘   │
│                                 │
├─────────────────────────────────┤
│ Navigation Buttons              │
│ [< Previous] [Next >]          │
└─────────────────────────────────┘
```

---

## 🔄 USER WORKFLOWS

### Workflow 1: Complete 3-Step Onboarding

```
Launch App
    ↓
Login/Authentication
    ↓
Landing Page (Choose: New Request / View History)
    ↓
Select "Start Onboarding"
    ↓
Step 1: Client Registration
  - Fill form + upload KYC docs
  - IPRS verification
  - Submit
    ↓
Step 2: Cheque Encashment
  - Fill applicant info
  - Add 1+ cheques
  - Upload supporting docs
  - Accept T&Cs
  - Submit
    ↓
Step 3: Official Use
  - Enter drawer verification
  - Enter account confirmation
  - Capture 5 signatures
  - Submit
    ↓
Success Screen (Show Request ID)
    ↓
Option: View History or Start New
```

### Workflow 2: Bond Request Wizard

```
Landing Page
    ↓
Select "Apply for Bond"
    ↓
Bond Step 1: Application Details
  - Principal/Applicant info
  - Beneficiary info
  - Guarantee amount & dates
  - Bond types selection
  - Signatory details
  - Upload documents
  - Submit
    ↓
Bond Step 2: Counter Guarantee/Indemnity
  - Display indemnity text
  - Capture indemnity date
  - Authorized signatory names
  - Indemnity signatures
  - Company seal/stamp
  - Submit
    ↓
Success Screen
    ↓
Return to Portal
```

### Workflow 3: Offline Draft & Sync

```
Start form (no network)
    ↓
User fills data
    ↓
Auto-save to AsyncStorage every 30 seconds
    ↓
Network lost indicator shown
    ↓
User closes app
    ↓
User relaunches app (network restored)
    ↓
"Resume draft?" prompt
    ↓
Load from AsyncStorage
    ↓
Submit to API when online
    ↓
Remove local draft after successful submission
```

---

## 📱 MOBILE-SPECIFIC FEATURES

### Platform-Specific Features

**iOS**
- Face ID / Touch ID for authentication
- iOS document picker (native)
- Share sheet for receipts
- Haptic feedback

**Android**
- Biometric API for fingerprint
- Android file picker
- Share intent for receipts
- Vibration feedback

### Common Mobile Features

1. **Camera Integration**
   - Capture ID/document photos directly
   - Gallery access for existing photos
   - Image compression before upload

2. **Offline Mode**
   - Draft requests saved to AsyncStorage
   - Auto-sync when network available
   - Conflict resolution (local vs server)

3. **File Management**
   - Temporary file cleanup on app launch
   - Cache management (max 500MB)
   - Download/view uploaded documents

4. **Push Notifications** (Future)
   - Request approved/rejected
   - Document verification status
   - Signature requests

5. **Deep Linking**
   - `onwards://wizard/step/1` - Direct to Step 1
   - `onwards://history/[requestId]` - View transaction
   - `onwards://bond/step/1` - Direct to bond wizard

6. **QR Code** (Future)
   - Generate QR for request ID
   - Share request via QR
   - Scan for quick access

---

## 🔐 SECURITY & VALIDATION

### Input Validation

```typescript
// src/utils/validation.ts
export const validators = {
  email: (value: string) => /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value),
  phone: (value: string) => /^(\+254|0)7\d{8}$/.test(value), // Kenya format
  currency: (value: number) => value > 0 && Number.isFinite(value),
  date: (value: Date) => {
    const now = new Date();
    return value <= now && (now.getFullYear() - value.getFullYear()) < 10;
  },
  kraPin: (value: string) => /^[A-Z0-9]{11}$/.test(value),
  chequeNumber: (value: string) => /^\d{6,12}$/.test(value)
};
```

### Authentication & Storage

```typescript
// Secure storage for sensitive data
import * as SecureStore from 'expo-secure-store';

// Store auth token securely
await SecureStore.setItemAsync('authToken', token);

// Encrypt sensitive data before AsyncStorage
import { CryptoJS } from 'react-native-crypto-js';
const encrypted = CryptoJS.AES.encrypt(JSON.stringify(data), secret).toString();
await AsyncStorage.setItem('sensitiveData', encrypted);
```

### API Security

```typescript
// Axios interceptor for auth
axiosInstance.interceptors.request.use(async (config) => {
  const token = await SecureStore.getItemAsync('authToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// Handle 401 - refresh token or logout
axiosInstance.interceptors.response.use(
  (response) => response,
  async (error) => {
    if (error.response?.status === 401) {
      // Clear auth and redirect to login
      await SecureStore.deleteItemAsync('authToken');
      // Dispatch logout action
    }
    return Promise.reject(error);
  }
);
```

### File Upload Security

- Whitelist MIME types: `image/jpeg`, `image/png`, `application/pdf`, `application/msword`
- Max file size: 5MB per file
- Max total: 50MB
- Scan for viruses using backend API
- Use HTTPS only (certificate pinning optional)

---

## 🧪 TESTING STRATEGY

### Unit Tests (Jest)

```bash
# Test utilities
jest src/utils/validation.test.ts

# Test hooks
jest src/hooks/useForm.test.ts

# Test services
jest src/services/wizardService.test.ts
```

### Component Tests (React Native Testing Library)

```bash
# Test form components
jest src/components/forms/ClientForm.test.tsx

# Test screens
jest src/screens/onboarding/Step1Screen.test.tsx
```

### Integration Tests

```bash
# Mock API with MSW (Mock Service Worker)
# Test full wizard flow with mock data
jest src/__tests__/integration/
```

### E2E Tests (Detox for React Native)

```json
{
  "testRunner": "jest",
  "configurations": {
    "ios.sim.debug": {
      "type": "ios.simulator",
      "device": { "type": "iPhone 15" },
      "app": "ios.debug"
    },
    "android.emu.debug": {
      "type": "android.emulator",
      "device": { "avdName": "Pixel_4_API_31" },
      "app": "android.debug"
    }
  }
}
```

### Manual Testing Checklist

- [ ] Complete all 3 onboarding steps with valid data
- [ ] Form validation errors (submit with missing fields)
- [ ] File uploads (JPG, PNG, PDF - within size limits)
- [ ] Signature capture on different devices/orientations
- [ ] Network failure handling and retry
- [ ] Offline draft creation and online sync
- [ ] Multiple device testing (phones, tablets)
- [ ] Both iOS and Android platforms
- [ ] Accessibility (font sizes, contrast, screen reader)

---

## 📅 IMPLEMENTATION TIMELINE

### **Phase 1: Project Setup & Infrastructure (1-2 weeks)**

- Initialize React Native + Expo project
- Set up TypeScript, ESLint, Prettier
- Configure Redux + RTK Query
- Set up navigation structure
- API client setup with Axios
- AsyncStorage + Secure Store configuration
- **Deliverables**: Runnable app shell with navigation

### **Phase 2: Onboarding Step 1 (2 weeks)**

- Client model & Redux slice
- Step1 screen UI (form + file upload)
- Form validation
- IPRS integration
- KYC document upload service
- **Deliverables**: Complete Step 1 with IPRS verification

### **Phase 3: Onboarding Step 2 (2-3 weeks)**

- ChequeEncashmentRequest model & Redux slice
- Step2 screen UI with repeating cheques
- File upload service
- File picker integration
- Terms & conditions display
- **Deliverables**: Complete Step 2 with dynamic cheque addition

### **Phase 4: Onboarding Step 3 (2 weeks)**

- OfficialUseRecord model & Redux slice
- Step3 screen UI (5 signatories)
- Signature capture control (SVG + gesture-handler)
- Signature storage service
- **Deliverables**: Complete Step 3 with multi-level approvals

### **Phase 5: Bond Wizard (2 weeks)**

- Bond request models & Redux slices
- Bond Step 1 screen (application details)
- Bond Step 2 screen (indemnity/counter-guarantee)
- Signature capture for bond
- **Deliverables**: Complete bond wizard flow

### **Phase 6: History & View Features (1 week)**

- Transaction history screen
- Detail view for past requests
- Receipt generation / sharing
- Edit workflow (jump to specific step)
- **Deliverables**: View transaction history, drill into details

### **Phase 7: Offline & Sync (1-2 weeks)**

- AsyncStorage draft management
- Background sync service
- Conflict resolution
- Sync queue management
- **Deliverables**: Draft locally, sync when network available

### **Phase 8: Testing & Polish (2 weeks)**

- Unit tests (60%+ coverage)
- Integration tests
- E2E tests (Detox)
- Performance optimization
- Bug fixes
- **Deliverables**: Test suite, release-ready app

### **Phase 9: Build & Deployment (1 week)**

- Configure EAS Build
- iOS app signing
- Android app signing
- TestFlight distribution
- Google Play internal testing
- **Deliverables**: Apps ready for store submission

---

## ⏱️ TOTAL PROJECT TIMELINE

| Phase          | Duration        | Total Weeks |
| -------------- | --------------- | ----------- |
| Phase 1        | 1-2 weeks       | 2           |
| Phase 2        | 2 weeks         | 4           |
| Phase 3        | 2-3 weeks       | 7           |
| Phase 4        | 2 weeks         | 9           |
| Phase 5        | 2 weeks         | 11          |
| Phase 6        | 1 week          | 12          |
| Phase 7        | 1-2 weeks       | 14          |
| Phase 8        | 2 weeks         | 16          |
| Phase 9        | 1 week          | 17          |
| **TOTAL** | **17 weeks** | **17**  |

**Estimated Start**: June 2026
**Estimated Completion**: Late September 2026

**Buffer**: Additional 1-2 weeks for unforeseen issues, API changes, platform-specific issues

---

## 💼 RESOURCE REQUIREMENTS

### Development Team

- **1 Senior React Native Developer** (Full-time, 17 weeks)
- **1 Backend API Consultant** (Part-time, 2-3 weeks for API review/fixes)
- **1 QA/Tester** (Part-time, Weeks 8-17)

### Infrastructure & Tools

- **Development**: VS Code, Node.js 18+, React Native CLI, Expo CLI
- **Testing**: iOS simulator (Xcode), Android emulator (Android Studio)
- **Build**: EAS Build (Expo), CocoaPods (iOS), Gradle (Android)
- **Distribution**: TestFlight (iOS), Google Play Internal Testing (Android)
- **Version Control**: Git + GitHub
- **CI/CD**: GitHub Actions or EAS Build automation

### Required Services

- OnwardsSwift.API backend (already available)
- IPRS API access (Kenya national ID verification)
- SMTP for email notifications (SendGrid ready on backend)
- SMS notifications (Africa's Talking, configured on backend)

---

## 🎯 SUCCESS CRITERIA

- ✅ Both onboarding (3-step) and bond (2-step) wizards fully functional
- ✅ Form validation with < 2% false positives
- ✅ Average screen load time < 1.5 seconds
- ✅ Offline mode working (draft + sync when online)
- ✅ App size < 80MB (Android), < 100MB (iOS)
- ✅ 60%+ code coverage (unit + integration tests)
- ✅ All E2E scenarios passing on iOS + Android
- ✅ TestFlight + Google Play internal testing approval
- ✅ Zero critical bugs before store submission
- ✅ Accessibility score > 90

---

## 🚀 FUTURE ENHANCEMENTS

1. **Biometric Login**: Fingerprint/Face ID on both platforms
2. **Push Notifications**: Real-time request status updates
3. **Multi-Language**: Localization (Swahili, French, Arabic)
4. **Dark Mode**: Support system theme preference
5. **Document Preview**: In-app PDF/image viewer
6. **Real-Time Sync**: WebSocket updates for collaborative approvals
7. **Advanced Analytics**: Usage tracking & performance monitoring
8. **Payment Integration**: Online payment gateway for fees
9. **Accessibility**: WCAG 2.1 AA compliance
10. **Biometric Signatures**: Fingerprint-based signature capture

---

## 🔗 BACKEND API INTEGRATION

### Required Endpoints (OnwardsSwift.API)

The following endpoints must be available or created:

#### Authentication
- `POST /api/auth/login` — User login
- `POST /api/auth/refresh` — Refresh token
- `POST /api/auth/logout` — User logout

#### Client Management
- `POST /api/clients` — Create client
- `GET /api/clients/{id}` — Get client details
- `GET /api/clients/search?name={name}` — Search clients

#### IPRS Verification
- `GET /api/iprs/{idNumber}` — Verify Kenya national ID

#### Cheque Encashment
- `POST /api/cheques/request` — Create encashment request
- `POST /api/cheques/{id}/items` — Add cheques to request
- `POST /api/cheques/{id}/attachments` — Upload supporting docs
- `GET /api/cheques/{id}` — Get encashment request details
- `GET /api/cheques` — List all requests

#### Official Use / Approvals
- `POST /api/officialuse/{id}` — Submit approvals
- `GET /api/officialuse/{id}` — Get approval record

#### Bond Requests
- `POST /api/bond-requests` — Create bond request
- `POST /api/bond-requests/{id}/indemnity` — Submit indemnity
- `POST /api/bond-requests/{id}/status` — Update bond status
- `GET /api/bond-requests/{id}` — Get bond request details
- `GET /api/bond-requests` — List all bond requests

#### Transaction History
- `GET /api/transactions` — List user's transactions
- `GET /api/transactions/{id}` — Get full transaction details

---

## 📦 BUILD & DEPLOYMENT

### Build Configuration (app.json)

```json
{
  "expo": {
    "name": "OnwardsSwift",
    "slug": "onwards-swift",
    "version": "1.0.0",
    "assetBundlePatterns": ["**/*"],
    "ios": {
      "supportsTabletMode": true,
      "bundleIdentifier": "com.ondwardswift.mobile",
      "buildNumber": "1"
    },
    "android": {
      "package": "com.ondwardswift.mobile",
      "versionCode": 1,
      "permissions": [
        "CAMERA",
        "READ_EXTERNAL_STORAGE",
        "WRITE_EXTERNAL_STORAGE"
      ]
    }
  }
}
```

### EAS Build Configuration (eas.json)

```json
{
  "build": {
    "preview": {
      "android": { "buildType": "apk" },
      "ios": { "buildType": "simulator" }
    },
    "preview2": {
      "android": { "buildType": "apk" },
      "ios": { "buildType": "simulator" }
    },
    "production": {
      "android": { "buildType": "app-bundle" },
      "ios": { "buildType": "archive" }
    }
  }
}
```

### Build Commands

```bash
# Development build (local testing)
eas build --platform ios --profile preview
eas build --platform android --profile preview

# Production builds (store submission)
eas build --platform ios --profile production
eas build --platform android --profile production
```

---

## 📞 CONTACT & SUPPORT

For implementation questions or API clarification:

1. Review OnwardsSwift.API documentation ([BondRequestsController.cs](OnwardsSwift.API/Controllers/BondRequestsController.cs), [ClientsController.cs](OnwardsSwift.API/Controllers/ClientsController.cs), etc.)
2. Check API test examples in Postman collection
3. Refer to CLAUDE.md for build & run commands
4. Review existing MAUI specification for feature parity

---

## 📋 REQUIREMENTS CHECKLIST FOR REACT NATIVE AGENT

### Critical Requirements

- [ ] **React Native 0.74+** with Expo support
- [ ] **TypeScript** for type safety
- [ ] **Redux Toolkit** for state management
- [ ] **React Navigation** 6.x for app navigation
- [ ] **React Hook Form + Zod** for form validation
- [ ] **Axios** with interceptors for API calls
- [ ] **AsyncStorage** for local data persistence
- [ ] **Secure storage** for auth tokens (Expo SecureStore)
- [ ] **Document Picker** (Expo) for file upload
- [ ] **Image Picker** (Expo) for camera/gallery access
- [ ] **Custom Signature Pad** using react-native-svg + gesture-handler

### Screen Requirements

**Onboarding Wizard Screens**:
- [ ] Step 1: Client Registration (form + KYC upload)
- [ ] Step 2: Cheque Encashment (repeating cheques + files)
- [ ] Step 3: Official Use (5-level approvals)
- [ ] Success confirmation screen

**Bond Wizard Screens**:
- [ ] Bond Step 1: Application details (signatories + documents)
- [ ] Bond Step 2: Counter guarantee/indemnity (signatures)
- [ ] Success confirmation screen

**Supporting Screens**:
- [ ] Authentication (login)
- [ ] Transaction history (list)
- [ ] Detail view (full transaction)
- [ ] Settings/Profile

### Feature Requirements

- [ ] **Form Validation**: Real-time client-side + server-side validation
- [ ] **File Upload**: Support JPG, PNG, PDF, DOCX (max 5MB per file, 50MB total)
- [ ] **IPRS Integration**: Kenya national ID verification with success badge
- [ ] **Signature Capture**: Touch-based drawing on all devices
- [ ] **Offline Mode**: Draft locally in AsyncStorage, sync when online
- [ ] **Error Handling**: Network failures, validation errors, API errors
- [ ] **Loading States**: Show spinners during API calls
- [ ] **Success Feedback**: Toast notifications + success screens
- [ ] **Deep Linking**: Navigate to specific screens via URL schemes

### API Integration Requirements

- [ ] Authentication (login, token refresh, logout)
- [ ] All cheque encashment endpoints (create, add items, upload files)
- [ ] All official use endpoints (submit approvals)
- [ ] All bond request endpoints (create, submit indemnity, update status)
- [ ] Transaction history endpoints (list, view details)
- [ ] IPRS verification endpoint

### Testing Requirements

- [ ] Unit tests (Jest): > 60% code coverage
- [ ] Component tests (React Native Testing Library)
- [ ] Integration tests (mock API with MSW)
- [ ] E2E tests (Detox) for critical user flows
- [ ] Manual testing on iOS + Android devices

### Deployment Requirements

- [ ] Configure EAS Build for automated builds
- [ ] Set up iOS app signing + provisioning profiles
- [ ] Set up Android app signing + keystore
- [ ] TestFlight distribution setup (iOS)
- [ ] Google Play internal testing setup (Android)
- [ ] Release notes & version management

---

## 🎓 Developer Notes

1. **Avoid** using Expo-only features if native app store distribution is required
2. **Use** EAS Build instead of local builds for consistency across platforms
3. **Implement** error boundaries to catch and report crashes
4. **Add** analytics tracking for user flows and errors
5. **Keep** navigation state persistent across app restarts
6. **Test** on actual devices, not just simulators (simulator ≠ real device)
7. **Monitor** app performance using React Native DevTools and Flipper
8. **Document** all custom hooks and utility functions
9. **Use** code splitting and lazy loading for large screens
10. **Implement** proper loading and error states for all async operations

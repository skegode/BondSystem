# IPRS Integration Implementation Summary

## What Was Implemented

### 1. **IPRS Service Layer** (`OnwardsSwift.Infrastructure/Services/IprsService.cs`)
A complete, production-ready Spinmobile IPRS integration service with:
- **OAuth-like token authentication**: Obtains access tokens using consumer key/secret
- **Identity verification endpoint**: Calls `/analytics/account/iprs` with search_type="identity"
- **Token caching**: Maintains token with 60-second buffer before expiry
- **Error handling**: Comprehensive logging and user-friendly error messages
- **Response parsing**: Extracts ID number, full name, gender, and date of birth from IPRS response

### 2. **Service Registration** (`Program.cs`)
- Registered `IIprsService` as a typed HttpClient with 30-second timeout
- Properly configured dependency injection for the controller

### 3. **Configuration** (`appsettings.json`)
Added IPRS configuration section:
```json
"Iprs": {
  "BaseUrl": "https://sandbox-api.spinmobile.co",
  "ConsumerKey": "YOUR_CONSUMER_KEY_HERE",
  "ConsumerSecret": "YOUR_CONSUMER_SECRET_HERE",
  "TimeoutSeconds": 30
}
```

### 4. **Controller Update** (`OnboardingController.cs`)
- Updated `VerifyIprs` endpoint to call the real IPRS service instead of mock
- Logs all verification attempts for audit/compliance
- Returns full IPRS response data (ID, full name, reference ID)

### 5. **UI Enhancement** (`OnboardingWizard.cshtml`)
#### New Identity Verification Section
- Dedicated input fields for National ID Number and Full Name
- Clear "Verify" button with visual feedback
- Success summary card showing verified data
- Clear/Reset button to allow re-verification

#### Auto-Population
- On successful verification, the system automatically populates:
  - `CompanyName` (Full Name from IPRS)
  - `IdNumber` (National ID from IPRS)
  - `KraPin` (if available from IPRS response)
- Updates verification status in form state

#### Form Submission Requirement
- **Form submission is blocked** until IPRS verification is completed
- User is prompted with a clear message if they try to submit without verification
- Verification status is carried through to Step 2

### 6. **JavaScript Implementation**
- Event handlers for identity verification workflow
- Real-time status updates with Bootstrap alerts
- Automatic form field population from IPRS data
- Form validation that prevents submission without verification

---

## Next Steps: Add Your Credentials

You now need to add your Spinmobile consumer key and secret to the configuration:

### Edit `OnwardsSwift.API/appsettings.json`:
```json
"Iprs": {
  "BaseUrl": "https://sandbox-api.spinmobile.co",
  "ConsumerKey": "YOUR_ACTUAL_CONSUMER_KEY",
  "ConsumerSecret": "YOUR_ACTUAL_CONSUMER_SECRET",
  "TimeoutSeconds": 30
}
```

Replace:
- `YOUR_ACTUAL_CONSUMER_KEY` with your Spinmobile consumer key
- `YOUR_ACTUAL_CONSUMER_SECRET` with your Spinmobile consumer secret

### Optionally Update Base URL
If you have a production endpoint, update the `BaseUrl`:
```json
"BaseUrl": "https://api.spinmobile.co"  // for production
```

---

## Testing the Integration

1. **Start the application**:
   ```bash
   dotnet run --project OnwardsSwift.API
   ```

2. **Navigate to Client Onboarding Wizard**:
   - Go to Forms → Client Onboarding
   - Proceed to Step 1 (Client Information)

3. **Complete Identity Verification**:
   - Fill in a valid Kenyan national ID number
   - Enter the full name exactly as it appears on the ID
   - Click "Verify"
   - System will call IPRS and return results
   - Verified data auto-populates in the form
   - Form becomes ready for submission to Step 2

4. **Verify Audit Trail**:
   - Check `App_Data/verification-log.json` for IPRS verification records
   - Check `App_Data/audit-logs.json` for compliance logging

---

## Architecture Overview

```
Client Browser (OnboardingWizard.cshtml)
    ↓ [IPRS verification request]
OnboardingController.VerifyIprs()
    ↓ [Dependency Injection]
IIprsService.VerifyIdentityAsync()
    ↓ [Token authentication]
Spinmobile /analytics/auth/ endpoint
    ↓ [Returns access token]
IIprsService caches token
    ↓ [Verification request]
Spinmobile /analytics/account/iprs endpoint
    ↓ [Returns identity data]
OnboardingController [Logs result]
    ↓ [JSON response]
Client Browser [Auto-populates form]
```

---

## Features

✅ Real IPRS integration with Spinmobile  
✅ Token-based authentication with auto-refresh  
✅ Automatic form field population from verified IPRS data  
✅ Form submission blocking until verification complete  
✅ Audit logging for compliance  
✅ Comprehensive error handling and user feedback  
✅ Production-ready error handling and logging  
✅ Timeout handling for slow network conditions  

---

## Support & Troubleshooting

### Common Issues

**1. "Cannot authenticate with IPRS service"**
- Verify your consumer key and secret are correct
- Check that the BaseUrl is correct (sandbox vs production)
- Ensure your Spinmobile account has the IPRS product enabled

**2. "IPRS verification failed - no matching record found"**
- Confirm the ID number format is correct
- Ensure the full name matches exactly what's on the national ID
- Contact Spinmobile support to verify the ID is in their system

**3. Timeout errors**
- The default timeout is 30 seconds
- Adjust `TimeoutSeconds` in appsettings.json if needed
- Check your internet connection to the Spinmobile API

### Logging

All IPRS interactions are logged. Check your application logs for:
- Token authentication attempts
- IPRS verification requests/responses
- Errors and exceptions

---

## Data Security

- Consumer secret is stored in configuration and not logged
- IPRS responses are stored in audit logs for compliance
- All verification attempts are timestamped and tracked
- KRA PIN from IPRS is auto-populated but not sent over unencrypted channels


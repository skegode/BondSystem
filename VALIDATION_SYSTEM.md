# Form Validation System Documentation

## Overview

The OnwardsSwift system now includes a comprehensive form validation framework that provides:

- **Server-side validation** with detailed error messages
- **Client-side validation** with real-time field feedback
- **Error banners** that display exactly what needs to be fixed
- **Field highlighting** to visually indicate invalid inputs
- **Accessibility compliance** with proper error messaging

## Components

### 1. Server-Side Validation (`_ValidationErrors.cshtml`)

A reusable Razor partial component that displays all server-side validation errors in a clear, organized error banner.

**Features:**
- Displays all validation errors from `ModelState`
- Groups errors by field name
- Converts field names from camelCase to readable text
- Shows icon and clear formatting
- Dismissible alert

**Usage:**
```html
@await Html.PartialAsync("_ValidationErrors")
```

**Example Output:**
```
⚠️ Please fix the following errors:

• ClientId: The Client field is required.
• TenderNumber: The Tender Number field is required.
• Amount: The Amount must be greater than 0.
```

### 2. Client-Side Validation (`form-validation.js`)

JavaScript module that validates forms in real-time as users interact with fields.

**Features:**
- Real-time validation on blur, change, and input events
- Debounced input validation (500ms delay)
- Type-specific validation:
  - **Email**: Validates email format
  - **Number**: Checks numeric values and min/max bounds
  - **Date**: Validates date format
  - **File**: Checks file type and extension
  - **Text**: Checks required fields
- Prevents form submission if validation fails
- Scrolls to first invalid field on submit
- Displays field-specific error messages

**Validation Rules:**

| Type | Rules |
|------|-------|
| Required | Field cannot be empty |
| Email | Must match valid email pattern |
| Number | Must be numeric, respect min/max attributes |
| Date | Must be valid ISO date format |
| File | Must match accepted file types |

### 3. Validation Styling (`validation.css`)

Enhanced CSS for visual validation feedback.

**Visual Indicators:**
- **Invalid fields**: Red border + error icon
- **Valid fields**: Green border + checkmark icon
- **Error messages**: Red text with icon below field
- **Error banner**: Red-tinted alert box with left border
- **Focus states**: Enhanced outline for accessibility

## Implementation in Forms

### BidBonds Create (`Views/BidBonds/Create.cshtml`)

The form now includes:
```html
<!-- Error banner at top of form -->
@await Html.PartialAsync("_ValidationErrors")

<form id="bondForm" method="post" asp-action="Create" enctype="multipart/form-data">
    <!-- Form fields with validation -->
    <input type="text" asp-for="TenderName" required />
    <select asp-for="ClientId" required>
        <option value="">-- Select Client --</option>
    </select>
    <input type="number" asp-for="Amount" step="0.01" required />
    <input type="date" asp-for="TenderClosingDate" required />
</form>
```

### Cheques Create (`Views/Cheques/Create.cshtml`)

```html
<!-- Validation error banner -->
@await Html.PartialAsync("_ValidationErrors")

<form asp-action="Create" method="post" enctype="multipart/form-data">
    <!-- Form fields -->
    <select name="clientId" required>
        <option value="">— Select Client —</option>
    </select>
    <input type="text" name="chequeNumber" required />
    <input type="number" id="txtAmount" name="chequeAmount" required />
</form>
```

## How It Works

### Submission Flow

1. **User fills form** → Real-time validation on each field
2. **User submits form** → Client-side validation runs
3. **If invalid** → Error banner appears + first field is focused
4. **If valid** → Form POSTs to server
5. **Server validates** → ModelState errors returned if invalid
6. **If server errors** → Error banner displays at top of form
7. **User corrects** → Resubmits

### Real-Time Validation

As the user types/changes fields:

1. **On Blur**: Field loses focus → validates
2. **On Change**: Dropdown/checkbox changes → validates
3. **On Input**: Text field being typed → validates with 500ms debounce
4. **Immediate Feedback**: Error message appears below field

### Visual Feedback

**Invalid Field:**
```
┌─────────────────────┐
│ Field name       ❌  │ ← Red border + error icon
└─────────────────────┘
❌ This field is required
```

**Valid Field:**
```
┌─────────────────────┐
│ Field name       ✓   │ ← Green border + checkmark
└─────────────────────┘
```

## Error Messages

The system provides user-friendly error messages:

### Default Messages

| Validation | Message |
|-----------|---------|
| Required | `{Field} is required` |
| Email | `{Field} must be a valid email address` |
| Number | `{Field} must be a valid number` |
| Min Value | `{Field} must be at least {min}` |
| Max Value | `{Field} cannot exceed {max}` |
| Date | `{Field} must be a valid date` |
| File Type | `{Field} has an invalid file type. Accepted: {types}` |

### Custom Messages (Server-Side)

You can provide custom error messages in controllers:

```csharp
[HttpPost]
public async Task<IActionResult> Create(CreateBidBondRequest model)
{
    if (string.IsNullOrWhiteSpace(model.TenderNumber))
    {
        ModelState.AddModelError(nameof(model.TenderNumber), 
            "The tender number must be unique and cannot be blank.");
    }
    
    if (!ModelState.IsValid)
    {
        await PopulateFormLookups();
        return View(model);
    }
    
    // Process form...
}
```

## Customizing Validation

### Add Custom Client-Side Rule

```javascript
// In form-validation.js, in the validateField function
if (isValid && value) {
    switch (fieldType) {
        case 'text':
            if (field.name === 'TenderNumber') {
                // Custom validation for tender number
                if (!/^[A-Z]{2,4}-[A-Z]{2,4}-\d{4}$/.test(value)) {
                    isValid = false;
                    errorMessage = 'Tender number must match format: XX-XX-2024';
                }
            }
            break;
    }
}
```

### Add Server-Side Validation

```csharp
public IActionResult Create(CreateBidBondRequest model)
{
    // Custom business logic validation
    if (model.Amount < 10000)
    {
        ModelState.AddModelError(nameof(model.Amount), 
            "Bond amount must be at least KES 10,000.");
    }
    
    if (model.TenorDays > 365)
    {
        ModelState.AddModelError(nameof(model.TenorDays), 
            "Tenor cannot exceed 365 days.");
    }
    
    // ... rest of validation
}
```

## Accessibility Features

- **Error messages** use semantic HTML with proper roles
- **Color not the only indicator** - uses icons and text
- **Focus management** - autofocus to first invalid field
- **Keyboard navigation** - all controls keyboard accessible
- **Screen reader support** - proper ARIA labels on errors

## Browser Support

- ✅ Chrome/Edge (Latest)
- ✅ Firefox (Latest)
- ✅ Safari (Latest)
- ✅ Mobile browsers
- ⚠️ IE11 (Limited support)

## Performance Considerations

- **Debounced input validation** (500ms) reduces unnecessary validation runs
- **CSS classes** used for visual updates (no heavy DOM manipulation)
- **Event delegation** used where possible
- **Lazy validation** - only validates visible/required fields

## Testing Validation

### Test Cases

1. **Empty Required Field**
   - Leave field blank → submit
   - Expected: Error banner + field highlight

2. **Invalid Email**
   - Enter "notanemail" → blur
   - Expected: Real-time error message

3. **Number Out of Range**
   - Enter "50000" when max is "10000"
   - Expected: Field highlights + error on change

4. **File Type Mismatch**
   - Select .docx file when only .pdf accepted
   - Expected: Error message on change

5. **Multiple Errors**
   - Leave 3 required fields empty → submit
   - Expected: All 3 shown in error banner

## Troubleshooting

### Error banner not appearing

1. Check that form includes: `@await Html.PartialAsync("_ValidationErrors")`
2. Verify controller returns `View(model)` after validation failure
3. Check browser console for JavaScript errors

### Real-time validation not working

1. Ensure `form-validation.js` is loaded (check network tab)
2. Check that form fields have `required` attribute
3. Verify field has valid `name` or `id` attribute
4. Check browser console for errors

### Style not applying

1. Verify `validation.css` is linked in `_Layout.cshtml`
2. Check for CSS conflicts with other stylesheets
3. Use browser dev tools to inspect element styles

## Future Enhancements

- [ ] Async validation (check server for uniqueness)
- [ ] Custom validation rules engine
- [ ] Validation summary in sidebar
- [ ] Toast notifications for errors
- [ ] Error analytics/tracking
- [ ] Multi-language error messages
- [ ] Auto-save with validation
- [ ] Field dependency validation

## Support

For issues or questions:
1. Check the troubleshooting section
2. Review validation.js console output
3. Inspect network requests in browser dev tools
4. Contact development team with error details

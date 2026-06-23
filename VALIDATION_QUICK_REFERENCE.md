<!-- QUICK REFERENCE: Form Validation System -->

# Form Validation - Quick Start Guide

## For Users

### When Filling Forms

1. **Required Fields** show a red asterisk `*`
2. **As you type/interact** fields validate in real-time
3. **Error messages** appear below fields if something is wrong
4. **Red border** = invalid field
5. **Green border** = valid field
6. **On submit**, all errors show in a banner at the top

### Examples of What Gets Checked

| What | Example |
|-----|---------|
| Empty fields | "Client is required" |
| Email format | "Must be valid email like user@example.com" |
| Number bounds | "Must be at least 10000" or "Cannot exceed 365" |
| Date format | "Must be a valid date" |
| File type | "Only PDF files accepted for this field" |

## For Developers

### Add Validation to a Form

**Step 1:** Include the error banner at top of form
```html
@await Html.PartialAsync("_ValidationErrors")

<form method="post">
    <!-- Your form fields -->
</form>
```

**Step 2:** Add `required` attribute to fields that must be filled
```html
<input type="text" name="fieldName" required />
<select name="fieldName" required>...</select>
<input type="email" name="email" required />
```

**Step 3:** Done! Validation is automatic via `form-validation.js`

### Server-Side Custom Validation

In your controller:

```csharp
[HttpPost]
public async Task<IActionResult> Create(MyModel model)
{
    // Business logic validation
    if (model.Amount < 10000)
    {
        ModelState.AddModelError(nameof(model.Amount), 
            "Minimum amount is KES 10,000");
    }
    
    if (!ModelState.IsValid)
    {
        // Repopulate dropdowns if needed
        await PopulateFormLookups();
        return View(model);  // Returns with error banner
    }
    
    // Process valid form...
}
```

### Data Attributes for Validation

```html
<!-- Required field -->
<input type="text" name="field" required />

<!-- Email validation -->
<input type="email" name="email" required />

<!-- Number with min/max -->
<input type="number" name="amount" min="1000" max="1000000" required />

<!-- Date field -->
<input type="date" name="date" required />

<!-- File with type restriction -->
<input type="file" name="document" accept=".pdf" required />
```

## Error Banner Example

When validation fails, users see:

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
⚠️  Please fix the following errors:

• Client: The Client field is required.
• Tender Number: The Tender Number field is required.
• Amount: The Amount must be greater than 1000.

[×] (Dismiss)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
```

## Real-Time Field Validation

As they fill the form:

```
[ Client          ] ❌ Required
  ↓ (User selects client)
[ KCB Bank ▼     ] ✓ Valid

[ Tender # ]    ❌ Required
  ↓ (User types "TEN-2024-001")
[ TEN-2024-001 ] ✓ Valid

[ Amount ] ❌ Must be at least 10000
  ↓ (User enters "50000")
[ 50000 ] ✓ Valid
```

## Validation Flow

```
┌─────────────────┐
│ User fills form │
└────────┬────────┘
         │
         ├─→ Real-time validation
         │   (blur/change/input events)
         │
         ├─→ Field shows error or ✓
         │
┌────────▼────────────┐
│ User clicks Submit  │
└────────┬────────────┘
         │
         ├─→ Client-side validation runs
         │
    ┌────▼─────┐
    │ Invalid? │
    └────┬──┬──┘
         │  │
      YES  NO
         │  │
         │  └─→ POST to server ✓
         │
         └─→ Show error banner
             Highlight first field
             Scroll to error ✓
             User corrects
             Retry ✓
```

## Files Involved

| File | Purpose |
|------|---------|
| `Views/Shared/_ValidationErrors.cshtml` | Error banner display |
| `wwwroot/js/form-validation.js` | Real-time validation logic |
| `wwwroot/css/validation.css` | Error styling |
| `Views/Shared/_Layout.cshtml` | Loads validation assets |

## Supported Field Types

- ✅ Text (`<input type="text">`)
- ✅ Email (`<input type="email">`)
- ✅ Number (`<input type="number">`)
- ✅ Date (`<input type="date">`)
- ✅ File (`<input type="file">`)
- ✅ Select (`<select>`)
- ✅ Checkbox/Radio (`<input type="checkbox/radio">`)
- ✅ Textarea (`<textarea>`)

## Common Validation Rules

| Rule | Attribute | Example |
|------|-----------|---------|
| Required | `required` | `<input required />` |
| Email | `type="email"` | `<input type="email" required />` |
| Minimum number | `min="1000"` | `<input type="number" min="1000" />` |
| Maximum number | `max="100000"` | `<input type="number" max="100000" />` |
| File types | `accept=".pdf,.doc"` | `<input type="file" accept=".pdf" />` |

## Testing Validation

### Test Case: Empty Required Field

1. Go to create form
2. Leave a required field blank
3. Click Submit
4. **Expected**: Red error banner appears at top
5. **Expected**: Field shows red border + error message

### Test Case: Invalid Email

1. Enter "notanemail" in email field
2. Click outside field (blur)
3. **Expected**: Real-time error message appears

### Test Case: Number Out of Range

1. Amount field with `min="10000"`
2. Enter "5000"
3. **Expected**: Error "must be at least 10000"

### Test Case: Wrong File Type

1. File field accepts only `.pdf`
2. Select a `.jpg` file
3. **Expected**: Error "Invalid file type"

## Accessibility

- Keyboard navigation: Tab to fields, use arrow keys for selects
- Screen readers: Errors announced with ARIA labels
- Color + icons: Not reliant on color alone
- Focus management: Autofocus to first error on submit

## Tips for Best UX

1. **Don't hide errors** - Always show clear messages
2. **Be specific** - "Amount must be at least 10000" not just "Invalid"
3. **Highlight fields** - Use colors to show invalid fields
4. **Scroll to error** - Automatically scroll first invalid field into view
5. **Real-time feedback** - Validate as users type, not just on submit
6. **Help text** - Add hints: "Format: YYYY-MM-DD" for dates

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Validation not working | Check that form has `required` attributes |
| Error banner not showing | Verify `_ValidationErrors` partial is included |
| Real-time validation slow | It's debounced at 500ms - that's normal |
| Errors not clearing | Clear form or reload page |
| Submit still fails | Server-side errors returned - fix those messages |

---

**For detailed documentation, see:** `VALIDATION_SYSTEM.md`

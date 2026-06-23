/**
 * Form Validation & Error Display Handler
 * Provides real-time validation feedback and highlights invalid fields
 */

document.addEventListener('DOMContentLoaded', function() {
    const forms = document.querySelectorAll('form[method="post"]');
    
    forms.forEach(form => {
        setupFormValidation(form);
    });
});

function setupFormValidation(form) {
    // Get all required fields
    const requiredFields = form.querySelectorAll('[required]');
    
    // Add real-time validation listeners
    requiredFields.forEach(field => {
        field.addEventListener('blur', function() {
            validateField(this);
        });
        
        field.addEventListener('change', function() {
            validateField(this);
        });
        
        // For text inputs, validate on input with debounce
        if (field.type === 'text' || field.type === 'email' || field.type === 'number') {
            field.addEventListener('input', debounce(function() {
                validateField(this);
            }, 500));
        }
    });
    
    // Validate form before submission
    form.addEventListener('submit', function(e) {
        let isValid = true;
        const fieldsToValidate = form.querySelectorAll('[required]');
        
        fieldsToValidate.forEach(field => {
            if (!validateField(field)) {
                isValid = false;
            }
        });
        
        if (!isValid) {
            e.preventDefault();
            scrollToFirstError(form);
            showValidationSummary(form);
        }
    });
}

function validateField(field) {
    let isValid = true;
    let errorMessage = '';
    
    const value = field.value.trim();
    const fieldName = getDisplayName(field.name || field.id);
    const fieldType = field.type;
    
    // Skip if field is hidden or disabled
    if (field.style.display === 'none' || field.disabled) {
        clearFieldError(field);
        return true;
    }
    
    // Basic required check
    if (!value && field.hasAttribute('required')) {
        isValid = false;
        errorMessage = `${fieldName} is required`;
    }
    
    // Type-specific validation
    if (isValid && value) {
        switch (fieldType) {
            case 'email':
                if (!isValidEmail(value)) {
                    isValid = false;
                    errorMessage = `${fieldName} must be a valid email address`;
                }
                break;
                
            case 'number':
                const numValue = parseFloat(value);
                if (isNaN(numValue)) {
                    isValid = false;
                    errorMessage = `${fieldName} must be a valid number`;
                } else if (field.min && numValue < parseFloat(field.min)) {
                    isValid = false;
                    errorMessage = `${fieldName} must be at least ${field.min}`;
                } else if (field.max && numValue > parseFloat(field.max)) {
                    isValid = false;
                    errorMessage = `${fieldName} cannot exceed ${field.max}`;
                }
                break;
                
            case 'date':
                if (!isValidDate(value)) {
                    isValid = false;
                    errorMessage = `${fieldName} must be a valid date`;
                }
                break;
                
            case 'file':
                if (field.hasAttribute('required') && !value) {
                    isValid = false;
                    errorMessage = `${fieldName} is required`;
                } else if (value && field.accept) {
                    if (!isValidFileType(value, field.accept)) {
                        isValid = false;
                        errorMessage = `${fieldName} has an invalid file type. Accepted: ${field.accept}`;
                    }
                }
                break;
        }
    }
    
    // Update field styling
    if (isValid) {
        clearFieldError(field);
    } else {
        showFieldError(field, errorMessage);
    }
    
    return isValid;
}

function showFieldError(field, message) {
    // Remove existing error indicator if any
    clearFieldError(field);
    
    // Add error styling to field
    field.classList.add('is-invalid');
    field.classList.remove('is-valid');
    
    // Add error class to parent container
    const container = field.closest('.mb-3, .col-md-6, .col-md-4, .col-md-5, .col-12, .row');
    if (container) {
        container.classList.add('field-error');
    }
    
    // Create error message element
    const errorEl = document.createElement('div');
    errorEl.className = 'invalid-feedback d-block small mt-1 text-danger';
    errorEl.setAttribute('data-error-for', field.name || field.id);
    errorEl.innerHTML = `<i class="bi bi-exclamation-circle me-1"></i>${message}`;
    
    // Insert after the field or its wrapper
    const wrapper = field.closest('.input-group') || field.closest('.form-floating') || field;
    wrapper.parentNode.insertBefore(errorEl, wrapper.nextSibling);
}

function clearFieldError(field) {
    field.classList.remove('is-invalid');
    
    // Only add is-valid if the field has content
    if (field.value.trim()) {
        field.classList.add('is-valid');
    } else {
        field.classList.remove('is-valid');
    }
    
    // Remove error class from parent container
    const container = field.closest('.mb-3, .col-md-6, .col-md-4, .col-md-5, .col-12, .row');
    if (container) {
        container.classList.remove('field-error');
    }
    
    // Remove error message if exists
    const errorEl = field.parentNode.querySelector(
        `.invalid-feedback[data-error-for="${field.name || field.id}"]`
    ) || field.nextElementSibling;
    
    if (errorEl && errorEl.classList.contains('invalid-feedback')) {
        errorEl.remove();
    }
}

function showValidationSummary(form) {
    const existingAlert = form.querySelector('.alert-danger[role="alert"]');
    if (!existingAlert) {
        const alert = document.createElement('div');
        alert.className = 'alert alert-danger alert-dismissible fade show mb-3';
        alert.role = 'alert';
        alert.innerHTML = `
            <strong><i class="bi bi-exclamation-triangle-fill me-2"></i>Validation Required</strong>
            <p class="mb-0 mt-2 small">Please review the highlighted fields and correct any errors before submitting.</p>
            <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
        `;
        form.insertBefore(alert, form.firstChild);
    }
}

function scrollToFirstError(form) {
    const firstInvalid = form.querySelector('.is-invalid');
    if (firstInvalid) {
        firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' });
        // Small delay to ensure scroll completes before focus
        setTimeout(() => firstInvalid.focus(), 300);
    }
}

function getDisplayName(fieldName) {
    // Remove common prefixes
    let name = fieldName.replace(/^(Model\.)?/, '');
    
    // Convert camelCase/PascalCase to readable text
    name = name.replace(/([A-Z])/g, ' $1').trim();
    
    // Capitalize first letter of each word
    return name.split(' ')
        .map(word => word.charAt(0).toUpperCase() + word.slice(1).toLowerCase())
        .join(' ');
}

function isValidEmail(email) {
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email);
}

function isValidDate(dateStr) {
    const date = new Date(dateStr);
    return date instanceof Date && !isNaN(date);
}

function isValidFileType(fileName, acceptedTypes) {
    const ext = '.' + fileName.split('.').pop().toLowerCase();
    const types = acceptedTypes.split(',').map(t => t.trim().toLowerCase());
    return types.some(t => fileName.toLowerCase().endsWith(t.replace(/^\*/, '')));
}

function debounce(func, delay) {
    let timeoutId;
    return function(...args) {
        clearTimeout(timeoutId);
        timeoutId = setTimeout(() => func.apply(this, args), delay);
    };
}

// Export for use in other scripts if needed
window.FormValidator = {
    validateField,
    validateForm: (form) => {
        setupFormValidation(form);
    }
};

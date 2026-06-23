using Dapper;

namespace OnwardsSwift.API.MobileLocal.Services;

public class LocalSchemaBootstrapper
{
    private readonly LocalSqliteContext _context;
    private readonly ILogger<LocalSchemaBootstrapper> _logger;

    public LocalSchemaBootstrapper(LocalSqliteContext context, ILogger<LocalSchemaBootstrapper> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task EnsureReadyAsync()
    {
        using var conn = _context.CreateConnection();
        await conn.OpenAsync();

        await conn.ExecuteAsync("PRAGMA journal_mode=WAL;");
        await conn.ExecuteAsync("PRAGMA foreign_keys=ON;");

        var sql = @"
CREATE TABLE IF NOT EXISTS users (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    full_name TEXT NOT NULL,
    phone TEXT NOT NULL UNIQUE,
    email TEXT NULL UNIQUE,
    status TEXT NOT NULL DEFAULT 'active',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NULL
);

CREATE TABLE IF NOT EXISTS otp_requests (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    phone TEXT NOT NULL,
    purpose TEXT NOT NULL,
    otp_hash TEXT NOT NULL,
    expires_at_utc TEXT NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    status TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    consumed_at_utc TEXT NULL,
    metadata_json TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_otp_phone_purpose ON otp_requests(phone, purpose);

CREATE TABLE IF NOT EXISTS sessions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    jti TEXT NOT NULL UNIQUE,
    token_hash TEXT NOT NULL,
    expires_at_utc TEXT NOT NULL,
    is_revoked INTEGER NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL,
    revoked_at_utc TEXT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS iprs_verification_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NULL,
    id_number TEXT NOT NULL,
    first_name TEXT NULL,
    middle_name TEXT NULL,
    last_name TEXT NULL,
    date_of_birth TEXT NULL,
    success INTEGER NOT NULL,
    upstream_payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS kra_verification_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NULL,
    kra_pin TEXT NOT NULL,
    taxpayer_name TEXT NULL,
    success INTEGER NOT NULL,
    upstream_payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS cheque_requests (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    applicant_name TEXT NOT NULL,
    id_number TEXT NULL,
    postal_address TEXT NULL,
    phone TEXT NOT NULL,
    purpose TEXT NOT NULL,
    terms_accepted INTEGER NOT NULL,
    declarant_name TEXT NULL,
    declarant_role TEXT NULL,
    declarant_date TEXT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS cheque_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    cheque_number TEXT NOT NULL,
    amount REAL NOT NULL,
    dated TEXT NOT NULL,
    drawer TEXT NOT NULL,
    bank TEXT NOT NULL,
    branch TEXT NOT NULL,
    payee TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(request_id) REFERENCES cheque_requests(id)
);

CREATE TABLE IF NOT EXISTS cheque_attachments (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    attachment_type TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    file_size INTEGER NOT NULL,
    file_path TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(request_id) REFERENCES cheque_requests(id)
);

CREATE TABLE IF NOT EXISTS cheque_referees (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    full_name TEXT NOT NULL,
    phone TEXT NULL,
    relationship TEXT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(request_id) REFERENCES cheque_requests(id)
);

CREATE TABLE IF NOT EXISTS cheque_signatories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    full_name TEXT NOT NULL,
    designation TEXT NULL,
    phone TEXT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(request_id) REFERENCES cheque_requests(id)
);

CREATE TABLE IF NOT EXISTS official_use_records (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    request_id INTEGER NOT NULL,
    checked_by TEXT NULL,
    checked_signature_path TEXT NULL,
    checked_date TEXT NULL,
    confirmed_with TEXT NULL,
    designation TEXT NULL,
    building_street TEXT NULL,
    drawer_status TEXT NULL,
    reason_for_payment TEXT NULL,
    account_confirmed_by TEXT NULL,
    account_status TEXT NULL,
    head_of_trade_finance TEXT NULL,
    head_of_trade_signature_path TEXT NULL,
    head_of_trade_date TEXT NULL,
    in_charge_finance TEXT NULL,
    in_charge_finance_signature_path TEXT NULL,
    in_charge_finance_date TEXT NULL,
    ceo TEXT NULL,
    ceo_signature_path TEXT NULL,
    ceo_date TEXT NULL,
    paid_by_name TEXT NULL,
    paid_by_signature_path TEXT NULL,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NULL,
    FOREIGN KEY(request_id) REFERENCES cheque_requests(id)
);

CREATE TABLE IF NOT EXISTS bond_applications (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    applicant_name TEXT NOT NULL,
    phone TEXT NOT NULL,
    email TEXT NULL,
    id_number TEXT NULL,
    tender_name TEXT NULL,
    tender_number TEXT NULL,
    procuring_entity TEXT NULL,
    amount REAL NULL,
    currency TEXT NULL,
    tenor_days INTEGER NULL,
    indemnity_text TEXT NULL,
    status TEXT NOT NULL DEFAULT 'pending',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE TABLE IF NOT EXISTS bond_application_types (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    application_id INTEGER NOT NULL,
    type_code TEXT NOT NULL,
    type_name TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(application_id) REFERENCES bond_applications(id)
);

CREATE TABLE IF NOT EXISTS bond_signatories (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    application_id INTEGER NOT NULL,
    full_name TEXT NOT NULL,
    designation TEXT NULL,
    phone TEXT NULL,
    id_number TEXT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(application_id) REFERENCES bond_applications(id)
);

CREATE TABLE IF NOT EXISTS bond_indemnitors (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    application_id INTEGER NOT NULL,
    full_name TEXT NOT NULL,
    id_number TEXT NULL,
    phone TEXT NULL,
    email TEXT NULL,
    address TEXT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(application_id) REFERENCES bond_applications(id)
);

CREATE TABLE IF NOT EXISTS bond_supporting_documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    application_id INTEGER NOT NULL,
    document_type TEXT NOT NULL,
    file_name TEXT NOT NULL,
    content_type TEXT NOT NULL,
    file_size INTEGER NOT NULL,
    file_path TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    FOREIGN KEY(application_id) REFERENCES bond_applications(id)
);

CREATE TABLE IF NOT EXISTS transaction_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    source_type TEXT NOT NULL,
    source_id INTEGER NOT NULL,
    title TEXT NOT NULL,
    amount REAL NULL,
    status TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    metadata_json TEXT NULL,
    FOREIGN KEY(user_id) REFERENCES users(id)
);

CREATE INDEX IF NOT EXISTS idx_tx_user_created ON transaction_history(user_id, created_at_utc);
";

        await conn.ExecuteAsync(sql);
        _logger.LogInformation("Local mobile API SQLite schema initialized at {Path}", _context.DatabasePath);
    }
}

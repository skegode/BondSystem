using System.Data;
using Dapper;

namespace OnwardsSwift.API.SignupWizard;

public static class SignupWizardSchema
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static volatile bool _ensured;

    // Runs EnsureSql at most once per app lifetime. This DDL was previously re-executed
    // on every call site (including every 10-second NotificationOutboxDispatcher tick),
    // adding needless load to a database that already shows intermittent timeouts.
    // A failed attempt does not set the flag, so the next caller retries.
    public static async Task EnsureAsync(IDbConnection conn)
    {
        if (_ensured) return;

        await Gate.WaitAsync();
        try
        {
            if (_ensured) return;
            await conn.ExecuteAsync(EnsureSql);
            _ensured = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public const string EnsureSql = @"
IF OBJECT_ID('dbo.signup_wizard_profiles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.signup_wizard_profiles
    (
        wizard_session_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        full_name NVARCHAR(200) NULL,
        national_id NVARCHAR(64) NULL,
        email NVARCHAR(256) NULL,
        phone NVARCHAR(64) NULL,
        kra_pin NVARCHAR(64) NULL,
        gender NVARCHAR(32) NULL,
        postal_address NVARCHAR(300) NULL,
        physical_address NVARCHAR(500) NULL,
        step2_payload_json NVARCHAR(MAX) NULL,
        work_details_json NVARCHAR(MAX) NULL,
        signature_metadata_json NVARCHAR(MAX) NULL,
        photo_metadata_json NVARCHAR(MAX) NULL,
        identity_verified BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_identity_verified DEFAULT(0),
        verified_at_utc DATETIME2(7) NULL,
        iprs_reference NVARCHAR(100) NULL,
        iprs_payload_json NVARCHAR(MAX) NULL,
        current_step INT NOT NULL CONSTRAINT DF_signup_wizard_profiles_current_step DEFAULT(1),
        generated_username NVARCHAR(256) NULL,
        generated_pin_hash NVARCHAR(400) NULL,
        account_created BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_account_created DEFAULT(0),
        account_created_at_utc DATETIME2(7) NULL,
        wizard_status NVARCHAR(40) NOT NULL CONSTRAINT DF_signup_wizard_profiles_status DEFAULT('draft'),
        credentials_dispatched BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_credentials_dispatched DEFAULT(0),
        dispatched_channels NVARCHAR(200) NULL,
        dispatched_at_utc DATETIME2(7) NULL,
        system_user_id UNIQUEIDENTIFIER NULL,
        system_user_id_int INT NULL,
        correlation_id NVARCHAR(128) NULL,
        created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_signup_wizard_profiles_created_at DEFAULT(SYSUTCDATETIME()),
        updated_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_signup_wizard_profiles_updated_at DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_signup_wizard_profiles_national_id ON dbo.signup_wizard_profiles(national_id);
    CREATE INDEX IX_signup_wizard_profiles_phone ON dbo.signup_wizard_profiles(phone);
    CREATE INDEX IX_signup_wizard_profiles_email ON dbo.signup_wizard_profiles(email);
END;

IF OBJECT_ID('dbo.notification_outbox', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_outbox
    (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        wizard_session_id UNIQUEIDENTIFIER NOT NULL,
        channel NVARCHAR(20) NOT NULL,
        recipient NVARCHAR(256) NOT NULL,
        subject NVARCHAR(300) NULL,
        message_body NVARCHAR(MAX) NOT NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_outbox_status DEFAULT('pending'),
        attempts INT NOT NULL CONSTRAINT DF_notification_outbox_attempts DEFAULT(0),
        max_attempts INT NOT NULL CONSTRAINT DF_notification_outbox_max_attempts DEFAULT(5),
        last_error NVARCHAR(1000) NULL,
        created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_created_at DEFAULT(SYSUTCDATETIME()),
        next_attempt_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_next_attempt DEFAULT(SYSUTCDATETIME()),
        sent_at_utc DATETIME2(7) NULL,
        updated_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_updated_at DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_notification_outbox_status_next ON dbo.notification_outbox(status, next_attempt_at_utc);
    CREATE INDEX IX_notification_outbox_session ON dbo.notification_outbox(wizard_session_id);
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_dispatch_logs (
        id BIGINT IDENTITY(1,1) PRIMARY KEY,
        outbox_id BIGINT NULL,
        otp_request_id BIGINT NULL,
        channel NVARCHAR(20) NOT NULL,
        destination NVARCHAR(256) NOT NULL,
        status NVARCHAR(20) NOT NULL,
        provider_response NVARCHAR(MAX) NULL,
        error_message NVARCHAR(MAX) NULL,
        created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME())
    );

    CREATE INDEX IX_notification_dispatch_logs_outbox_id ON dbo.notification_dispatch_logs(outbox_id);
    CREATE INDEX IX_notification_dispatch_logs_otp_request_id ON dbo.notification_dispatch_logs(otp_request_id);
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'outbox_id') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD outbox_id BIGINT NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'otp_request_id') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD otp_request_id BIGINT NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'channel') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD channel NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_channel DEFAULT('sms') WITH VALUES;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'destination') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD destination NVARCHAR(256) NOT NULL CONSTRAINT DF_notification_dispatch_logs_destination DEFAULT('') WITH VALUES;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'status') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD status NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_status DEFAULT('sent') WITH VALUES;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'provider_response') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD provider_response NVARCHAR(MAX) NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'error_message') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD error_message NVARCHAR(MAX) NULL;
END;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'created_at_utc') IS NULL
BEGIN
    ALTER TABLE dbo.notification_dispatch_logs ADD created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;
END;
";
}

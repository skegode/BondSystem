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
END;

IF COL_LENGTH('dbo.signup_wizard_profiles', 'full_name') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD full_name NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'national_id') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD national_id NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'email') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD email NVARCHAR(256) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'phone') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD phone NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'kra_pin') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD kra_pin NVARCHAR(64) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'gender') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD gender NVARCHAR(32) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'postal_address') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD postal_address NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'physical_address') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD physical_address NVARCHAR(500) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'step2_payload_json') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD step2_payload_json NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'work_details_json') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD work_details_json NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'signature_metadata_json') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD signature_metadata_json NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'photo_metadata_json') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD photo_metadata_json NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'identity_verified') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD identity_verified BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_identity_verified DEFAULT(0) WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'verified_at_utc') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD verified_at_utc DATETIME2(7) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'iprs_reference') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD iprs_reference NVARCHAR(100) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'iprs_payload_json') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD iprs_payload_json NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'current_step') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD current_step INT NOT NULL CONSTRAINT DF_signup_wizard_profiles_current_step DEFAULT(1) WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'generated_username') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD generated_username NVARCHAR(256) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'generated_pin_hash') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD generated_pin_hash NVARCHAR(400) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'account_created') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD account_created BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_account_created DEFAULT(0) WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'account_created_at_utc') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD account_created_at_utc DATETIME2(7) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'wizard_status') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD wizard_status NVARCHAR(40) NOT NULL CONSTRAINT DF_signup_wizard_profiles_status DEFAULT('draft') WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'credentials_dispatched') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD credentials_dispatched BIT NOT NULL CONSTRAINT DF_signup_wizard_profiles_credentials_dispatched DEFAULT(0) WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'dispatched_channels') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD dispatched_channels NVARCHAR(200) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'dispatched_at_utc') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD dispatched_at_utc DATETIME2(7) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'system_user_id') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD system_user_id UNIQUEIDENTIFIER NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'system_user_id_int') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD system_user_id_int INT NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'correlation_id') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD correlation_id NVARCHAR(128) NULL;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'created_at_utc') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_signup_wizard_profiles_created_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH('dbo.signup_wizard_profiles', 'updated_at_utc') IS NULL
    ALTER TABLE dbo.signup_wizard_profiles ADD updated_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_signup_wizard_profiles_updated_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_signup_wizard_profiles_national_id' AND object_id = OBJECT_ID('dbo.signup_wizard_profiles'))
    CREATE INDEX IX_signup_wizard_profiles_national_id ON dbo.signup_wizard_profiles(national_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_signup_wizard_profiles_phone' AND object_id = OBJECT_ID('dbo.signup_wizard_profiles'))
    CREATE INDEX IX_signup_wizard_profiles_phone ON dbo.signup_wizard_profiles(phone);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_signup_wizard_profiles_email' AND object_id = OBJECT_ID('dbo.signup_wizard_profiles'))
    CREATE INDEX IX_signup_wizard_profiles_email ON dbo.signup_wizard_profiles(email);
GO

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

END;

IF COL_LENGTH('dbo.notification_outbox', 'wizard_session_id') IS NULL
    ALTER TABLE dbo.notification_outbox ADD wizard_session_id UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_notification_outbox_wizard_session_id DEFAULT('00000000-0000-0000-0000-000000000000') WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'channel') IS NULL
    ALTER TABLE dbo.notification_outbox ADD channel NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_outbox_channel DEFAULT('sms') WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'recipient') IS NULL
    ALTER TABLE dbo.notification_outbox ADD recipient NVARCHAR(256) NOT NULL CONSTRAINT DF_notification_outbox_recipient DEFAULT('') WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'subject') IS NULL
    ALTER TABLE dbo.notification_outbox ADD subject NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.notification_outbox', 'message_body') IS NULL
    ALTER TABLE dbo.notification_outbox ADD message_body NVARCHAR(MAX) NOT NULL CONSTRAINT DF_notification_outbox_message_body DEFAULT('') WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'status') IS NULL
    ALTER TABLE dbo.notification_outbox ADD status NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_outbox_status DEFAULT('pending') WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'attempts') IS NULL
    ALTER TABLE dbo.notification_outbox ADD attempts INT NOT NULL CONSTRAINT DF_notification_outbox_attempts DEFAULT(0) WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'max_attempts') IS NULL
    ALTER TABLE dbo.notification_outbox ADD max_attempts INT NOT NULL CONSTRAINT DF_notification_outbox_max_attempts DEFAULT(5) WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'last_error') IS NULL
    ALTER TABLE dbo.notification_outbox ADD last_error NVARCHAR(1000) NULL;
IF COL_LENGTH('dbo.notification_outbox', 'created_at_utc') IS NULL
    ALTER TABLE dbo.notification_outbox ADD created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_created_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'next_attempt_at_utc') IS NULL
    ALTER TABLE dbo.notification_outbox ADD next_attempt_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_next_attempt DEFAULT(SYSUTCDATETIME()) WITH VALUES;
IF COL_LENGTH('dbo.notification_outbox', 'sent_at_utc') IS NULL
    ALTER TABLE dbo.notification_outbox ADD sent_at_utc DATETIME2(7) NULL;
IF COL_LENGTH('dbo.notification_outbox', 'updated_at_utc') IS NULL
    ALTER TABLE dbo.notification_outbox ADD updated_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_outbox_updated_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notification_outbox_status_next' AND object_id = OBJECT_ID('dbo.notification_outbox'))
    CREATE INDEX IX_notification_outbox_status_next ON dbo.notification_outbox(status, next_attempt_at_utc);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_notification_outbox_session' AND object_id = OBJECT_ID('dbo.notification_outbox'))
    CREATE INDEX IX_notification_outbox_session ON dbo.notification_outbox(wizard_session_id);

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_dispatch_logs
    (
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
    ALTER TABLE dbo.notification_dispatch_logs ADD outbox_id BIGINT NULL;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'otp_request_id') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD otp_request_id BIGINT NULL;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'channel') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD channel NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_channel DEFAULT('sms') WITH VALUES;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'destination') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD destination NVARCHAR(256) NOT NULL CONSTRAINT DF_notification_dispatch_logs_destination DEFAULT('') WITH VALUES;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'status') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD status NVARCHAR(20) NOT NULL CONSTRAINT DF_notification_dispatch_logs_status DEFAULT('sent') WITH VALUES;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'provider_response') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD provider_response NVARCHAR(MAX) NULL;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'error_message') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD error_message NVARCHAR(MAX) NULL;

IF OBJECT_ID('dbo.notification_dispatch_logs', 'U') IS NOT NULL
   AND COL_LENGTH('dbo.notification_dispatch_logs', 'created_at_utc') IS NULL
    ALTER TABLE dbo.notification_dispatch_logs ADD created_at_utc DATETIME2(7) NOT NULL CONSTRAINT DF_notification_dispatch_logs_created_at DEFAULT(SYSUTCDATETIME()) WITH VALUES;
GO

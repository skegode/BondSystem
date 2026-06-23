-- AddSerializabilityIndexes.sql
-- Backstop unique indexes for write paths that were upgraded to SERIALIZABLE-isolation
-- check-then-write / read-then-write transactions (see MobileSqlController.AddChequeItem,
-- FormsController.OfficialUse, SignupWizardController.Step1Verify). The application-level
-- transaction is what actually prevents the race; these indexes are the schema-level guarantee
-- in case that ever changes, matching the existing ChequeEncashmentRequests/BondApplications
-- Reference pattern (see AddReferenceIdempotencyColumns.sql).
--
-- Each index is created in its own TRY/CATCH and only after confirming no pre-existing
-- duplicates violate it -- unlike Reference (a brand-new nullable column, always duplicate-free
-- at creation time), these constraints are being retrofitted onto columns that already took
-- unconstrained writes, so production may already contain rows that would violate them.
-- A table reporting duplicates is skipped (with a PRINT) rather than failing the whole script;
-- dedupe those rows manually, then re-run this script to pick up the skipped index.

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

-- 1) One cheque-number line item per encashment request.
IF OBJECT_ID(N'dbo.ChequeEncashmentCheques', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ChequeEncashmentCheques_RequestId_ChequeNumber')
BEGIN
    IF EXISTS (
        SELECT 1 FROM dbo.ChequeEncashmentCheques
        WHERE ChequeNumber IS NOT NULL
        GROUP BY RequestId, ChequeNumber
        HAVING COUNT(*) > 1
    )
    BEGIN
        PRINT 'Skipped UX_ChequeEncashmentCheques_RequestId_ChequeNumber: existing duplicate (RequestId, ChequeNumber) rows found -- dedupe first.';
    END
    ELSE
    BEGIN
        BEGIN TRY
            EXEC(N'CREATE UNIQUE INDEX UX_ChequeEncashmentCheques_RequestId_ChequeNumber
                ON dbo.ChequeEncashmentCheques(RequestId, ChequeNumber)
                WHERE ChequeNumber IS NOT NULL;');
            PRINT 'Created UX_ChequeEncashmentCheques_RequestId_ChequeNumber';
        END TRY
        BEGIN CATCH
            PRINT CONCAT('Could not create UX_ChequeEncashmentCheques_RequestId_ChequeNumber: ', ERROR_MESSAGE());
        END CATCH
    END;
END;

-- 2) One Official Use (Step 3) record per cheque encashment request.
IF OBJECT_ID(N'dbo.OfficialUseRecords', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OfficialUseRecords_RequestId')
BEGIN
    IF EXISTS (
        SELECT 1 FROM dbo.OfficialUseRecords
        WHERE RequestId IS NOT NULL
        GROUP BY RequestId
        HAVING COUNT(*) > 1
    )
    BEGIN
        PRINT 'Skipped UX_OfficialUseRecords_RequestId: existing duplicate RequestId rows found -- dedupe first.';
    END
    ELSE
    BEGIN
        BEGIN TRY
            EXEC(N'CREATE UNIQUE INDEX UX_OfficialUseRecords_RequestId
                ON dbo.OfficialUseRecords(RequestId)
                WHERE RequestId IS NOT NULL;');
            PRINT 'Created UX_OfficialUseRecords_RequestId';
        END TRY
        BEGIN CATCH
            PRINT CONCAT('Could not create UX_OfficialUseRecords_RequestId: ', ERROR_MESSAGE());
        END CATCH
    END;
END;

-- 3) One in-progress signup wizard session per national ID (account_created = 0). Once an
--    account is created the row falls outside the filter, so a person can start a fresh wizard
--    session later (e.g. a second device, or after abandoning the first attempt).
IF OBJECT_ID(N'dbo.signup_wizard_profiles', N'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_signup_wizard_profiles_national_id_inprogress')
BEGIN
    IF EXISTS (
        SELECT 1 FROM dbo.signup_wizard_profiles
        WHERE national_id IS NOT NULL AND account_created = 0
        GROUP BY national_id
        HAVING COUNT(*) > 1
    )
    BEGIN
        PRINT 'Skipped UX_signup_wizard_profiles_national_id_inprogress: existing duplicate in-progress national_id rows found -- dedupe first.';
    END
    ELSE
    BEGIN
        BEGIN TRY
            EXEC(N'CREATE UNIQUE INDEX UX_signup_wizard_profiles_national_id_inprogress
                ON dbo.signup_wizard_profiles(national_id)
                WHERE national_id IS NOT NULL AND account_created = 0;');
            PRINT 'Created UX_signup_wizard_profiles_national_id_inprogress';
        END TRY
        BEGIN CATCH
            PRINT CONCAT('Could not create UX_signup_wizard_profiles_national_id_inprogress: ', ERROR_MESSAGE());
        END CATCH
    END;
END;
